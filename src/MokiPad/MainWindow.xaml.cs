using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MokiPad.Controls;
using MokiPad.Core;
using MokiPad.Models;
using MokiPad.Services;

namespace MokiPad;

/// <summary>
/// 主窗口的“代码后置”部分。界面结构在 MainWindow.xaml 里，
/// 这里负责打开文件、切换标签、创建菜单和把解析结果交给阅读控件。
/// </summary>
/// <remarks>
/// WPF 会把 XAML 里带 <c>x:Name</c> 的元素生成为这个类的字段（例如 <c>Tabs</c>、<c>HeaderTitle</c>），
/// 所以代码里可以直接使用，而不需要手写 <c>FindName</c>。
/// 窗口使用了自定义标题栏（WindowChrome），最小化/最大化/关闭都是自己处理的按钮。
/// </remarks>
public partial class MainWindow
{
    /// <summary>打开的标签集合。实现 INotifyCollectionChanged，ListBox 会自动跟随增删。</summary>
    private readonly ObservableCollection<DocumentTab> documents = [];

    /// <summary>串行化“打开文件”，避免用户连续拖入多个文件时同时读盘。</summary>
    private readonly SemaphoreSlim openGate = new(1, 1);

    /// <summary>串行化 Markdown 解析，避免两篇长文同时占用处理器。</summary>
    private readonly SemaphoreSlim parseGate = new(1, 1);

    private readonly Preferences preferences;
    private DocumentTab? active;
    private readonly bool initialized;
    private ContextMenu? openMenu;
    private MarkdownPreview? preview;
    private CancellationTokenSource? renderCancellation;

    /// <summary>
    /// 创建窗口。执行顺序很重要：先应用主题和语言，再加载 XAML，
    /// 这样 XAML 里的 DynamicResource 一开始就能取到正确的颜色和文字。
    /// </summary>
    public MainWindow(Preferences? settings = null)
    {
        preferences = settings ?? Preferences.Load();

        // 主题画刷放在 Application.Resources 里；InitializeComponent 之前设置，
        // 窗口里的 DynamicResource 在第一次加载时就能解析到。
        ThemeService.Apply(preferences.Dark);
        UiText.Apply(preferences.Language);

        InitializeComponent();

        // ItemsSource 绑定到 ObservableCollection，增删标签会自动更新列表。
        Tabs.ItemsSource = documents;
        BindKeys();
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) => { preview?.CopySelection(); e.Handled = true; },
            (_, e) => e.CanExecute = preview?.HasMarkdownSelection == true));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.SelectAll, (_, e) => { preview?.SelectAllMarkdown(); e.Handled = true; },
            (_, e) => e.CanExecute = preview is not null && active is not null));

        // initialized 用来区分“启动时的初始选择”和“用户真的切换标签”，
        // 避免初始化阶段触发一次无意义的渲染。
        initialized = true;
        UpdateThemeButton(false);
        ShowWelcome();

        // 背景图解码会读文件，推迟到窗口空闲时再做，避免拖慢启动。
        _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ApplyBackground));
    }

    /// <summary>当前阅读器的垂直滚动位置；没有阅读器时为 0。</summary>
    private double PreviewOffset => preview is null ? 0 : FindChild<ScrollViewer>(preview)?.VerticalOffset ?? 0;

    public FrameworkElement VerificationSurface => Root;
    public int RealizedPreviewBlocks => preview?.RealizedCount ?? 0;
    public int TotalPreviewBlocks => preview?.Items.Count ?? 0;

    public int VerificationFootnoteBlocks =>
        preview?.Items.OfType<PreviewBlock>().Count(block => block.Kind == "footnote") ?? 0;

    public int VerificationTabCount => documents.Count;
    public int VerificationOpenRequests { get; private set; }

    public double VerificationScrollOffset =>
        preview is null ? 0 : FindChild<ScrollViewer>(preview)?.VerticalOffset ?? 0;

    /// <summary>
    /// 注册键盘快捷键。WPF 的快捷键由 RoutedCommand + CommandBinding + KeyBinding 组成：
    /// 命令本身不绑定具体控件，按键触发后由 CommandBinding 执行动作。
    /// </summary>
    private void BindKeys()
    {
        InputBindings.Add(new KeyBinding(ApplicationCommands.Copy, Key.C, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(ApplicationCommands.SelectAll, Key.A, ModifierKeys.Control));
        Bind(Key.Escape, ModifierKeys.None, () => preview?.ClearMarkdownSelection());
        Bind(Key.O, ModifierKeys.Control, () => OnOpen(this, new RoutedEventArgs()));
        Bind(Key.W, ModifierKeys.Control, () => CloseTab(active));
        Bind(Key.Tab, ModifierKeys.Control, () =>
        {
            if (documents.Count > 1) Tabs.SelectedIndex = (Tabs.SelectedIndex + 1) % documents.Count;
        });
        Bind(Key.F5, ModifierKeys.None, () => _ = ReloadAsync());
        Bind(Key.F11, ModifierKeys.None, () => OnMaximize(this, new RoutedEventArgs()));
        Bind(Key.Add, ModifierKeys.Control, () => Zoom(1));
        Bind(Key.Subtract, ModifierKeys.Control, () => Zoom(-1));
        Bind(Key.OemPlus, ModifierKeys.Control, () => Zoom(1));
        Bind(Key.OemMinus, ModifierKeys.Control, () => Zoom(-1));
        Bind(Key.D0, ModifierKeys.Control, () =>
        {
            preferences.FontSize = 16;
            ApplyReadingSize();
        });

        // 本地函数声明放在 return 之后：它不会被“执行”，只是为了把实现放到主流程下面。
        return;

        void Bind(Key key, ModifierKeys mods, Action action)
        {
            // RoutedCommand 是一个“命令对象”，本身不做事；
            // CommandBinding 决定命令执行时调用哪个方法，KeyBinding 把按键映射到命令。
            var command = new RoutedCommand();
            CommandBindings.Add(new CommandBinding(command, (_, _) => action()));
            InputBindings.Add(new KeyBinding(command, key, mods));
        }
    }

    /// <summary>
    /// 回到欢迎页：清空当前标签的阅读内容，隐藏路径信息。
    /// </summary>
    private void ShowWelcome()
    {
        active = null;
        renderCancellation?.Cancel();
        preview?.SetBlocks([]);
        Welcome.Visibility = Visibility.Visible;
        HeaderTitle.Text = "MokiPad";
        HeaderPath.Visibility = Visibility.Collapsed;
        PathText.Text = "";
        DocumentInfo.Text = "";
        Status.Text = UiText.Get("ReaderReady");
        Title = "MokiPad";
    }

    /// <summary>
    /// 用户切换标签时：保存旧标签的滚动位置，取消旧渲染，再渲染新标签。
    /// </summary>
    private void OnTabSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || Tabs.SelectedItem is not DocumentTab tab || active == tab) return;
        if (active is not null)
        {
            active.ScrollOffset = PreviewOffset;
            if (active.File.Text.Length > 2_000_000) active.Parsed = null;
        }

        renderCancellation?.Cancel();
        active = tab;
        Welcome.Visibility = Visibility.Collapsed;
        HeaderTitle.Text = tab.Name;
        HeaderTitle.ToolTip = tab.Name;
        HeaderPath.Text = tab.FilePath;
        HeaderPath.ToolTip = tab.FilePath;
        HeaderPath.Visibility = Visibility.Visible;
        PathText.Text = tab.FilePath;
        PathText.ToolTip = tab.FilePath;
        Title = tab.Name + " — MokiPad";
        UpdateDocumentInfo();
        preview?.SetBlocks([]);
        _ = RenderAsync();
    }

    /// <summary>
    /// 打开一个文件。相同路径已经打开时直接切过去，不重复读盘。
    /// </summary>
    public async Task OpenFileAsync(string path)
    {
        await openGate.WaitAsync();
        try
        {
            path = Path.GetFullPath(path);
            if (documents.FirstOrDefault(d => d.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)) is
                { } existing)
            {
                Tabs.SelectedItem = existing;
                return;
            }

            Status.Text = UiText.Get("Opening");
            var loaded = await Task.Run(() => FileService.ReadAsync(path));
            var tab = new DocumentTab { FilePath = path, File = loaded };
            documents.Add(tab);
            Tabs.SelectedItem = tab;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException
                                       or ArgumentException)
        {
            ShowError(ex);
        }
        finally
        {
            openGate.Release();
        }
    }

    /// <summary>
    /// 解析当前标签并交给阅读控件。解析放在线程池，避免卡住界面。
    /// </summary>
    public async Task RenderAsync()
    {
        if (active is not { } tab) return;
        if (tab.Parsed is { } cached)
        {
            ApplyParsed(tab, cached);
            return;
        }

        if (renderCancellation is not null) await renderCancellation.CancelAsync();
        var cancellation = new CancellationTokenSource();
        renderCancellation = cancellation;
        var token = cancellation.Token;
        Status.Text = UiText.Get("Rendering");
        try
        {
            await parseGate.WaitAsync(token);
            MarkdownResult result;
            try
            {
                result = await Task.Run(() => tab.IsMarkdown
                    ? MarkdownEngine.Parse(tab.File.Text, token)
                    : PlainText(tab.File.Text, token), token);
            }
            finally
            {
                parseGate.Release();
            }

            if (token.IsCancellationRequested || active != tab) return;
            tab.Parsed = result;
            ApplyParsed(tab, result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Status.Text = UiText.Get("Error") + ": " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(renderCancellation, cancellation)) renderCancellation = null;
            cancellation.Dispose();
        }
    }

    /// <summary>
    /// 纯文本文件按固定长度切块，避免一个超大字符串变成单个控件。
    /// </summary>
    private static MarkdownResult PlainText(string source, CancellationToken token)
    {
        var blocks = new List<PreviewBlock>();
        for (var i = 0; i < source.Length;)
        {
            token.ThrowIfCancellationRequested();
            var length = Math.Min(4096, source.Length - i);
            if (i + length < source.Length && char.IsHighSurrogate(source[i + length - 1])) length--;
            blocks.Add(new PreviewBlock("paragraph", 0, i, source.Substring(i, length), CopyStart: i, CopyEnd: i + length));
            i += length;
        }

        return new MarkdownResult(blocks, 0, source);
    }

    /// <summary>
    /// 第一次需要阅读器时创建它，并放进 XAML 里预留的 PreviewHost。
    /// </summary>
    private void EnsurePreview()
    {
        if (preview is not null) return;
        preview = new MarkdownPreview { ReadingFontSize = Math.Clamp(preferences.FontSize, 10, 28) };
        preview.ContextMenuOpening += OnReaderContextMenu;
        // 阅读器自己会抛出“用户点了本地链接”，这里把它接到打开文件的流程。
        preview.ContextMenu = new ContextMenu();
        preview.OpenFileRequested += path =>
        {
            VerificationOpenRequests++;
            _ = OpenFileAsync(path);
        };
        // PreviewHost 是 XAML 里的 ContentControl；把阅读器放进去后，
        // WPF 会自动完成测量、布局和绘制。
        preview.CopyFailed += () => Status.Text = UiText.Get("ClipboardBusy");
        PreviewHost.Child = new AdornerDecorator { Child = preview };
    }

    /// <summary>
    /// 把解析结果交给阅读器，并在布局完成后恢复该标签的滚动位置。
    /// </summary>
    private void ApplyParsed(DocumentTab tab, MarkdownResult result)
    {
        EnsurePreview();
        preview!.DocumentPath = tab.FilePath;
        preview.SetBlocks(result.Blocks, result.Source);
        Status.Text = UiText.Get("PreviewTime", result.Blocks.Count, result.ParseMs);
        var offset = tab.ScrollOffset;

        // Loaded 优先级表示“等新的列表项完成测量和布局之后再恢复位置”，
        // 否则这时 ScrollableHeight 还是 0，滚动会被吞掉。
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (active == tab && preview is not null) FindChild<ScrollViewer>(preview)?.ScrollToVerticalOffset(offset);
        }));
    }

    /// <summary>
    /// 更新底部状态栏中的字数与编码信息。
    /// </summary>
    private void UpdateDocumentInfo()
    {
        if (active is null) return;
        DocumentInfo.Text = UiText.Get("Characters", active.File.Text.Length) + " · " +
                            active.File.Encoding.WebName.ToUpperInvariant();
    }

    /// <summary>
    /// 关闭标签；全部关闭后回到欢迎页。
    /// </summary>
    private void CloseTab(DocumentTab? tab)
    {
        if (tab is null) return;
        documents.Remove(tab);
        if (documents.Count == 0) ShowWelcome();
        else if (Tabs.SelectedItem is null) Tabs.SelectedIndex = documents.Count - 1;
    }

    /// <summary>
    /// “打开”按钮/快捷键的入口。
    /// </summary>
    private void OnOpen(object sender, RoutedEventArgs e) => _ = OpenFilesAsync();

    /// <summary>
    /// 弹出文件选择框，并逐个打开选中的文件。
    /// </summary>
    private async Task OpenFilesAsync()
    {
        var dialog = new OpenFileDialog { Multiselect = true, Filter = UiText.Get("OpenFilter") };
        if (dialog.ShowDialog(this) != true) return;
        foreach (var path in dialog.FileNames) await OpenFileAsync(path);
    }

    /// <summary>
    /// 重新读取当前文件，丢弃旧解析结果后重新渲染。
    /// </summary>
    private async Task ReloadAsync()
    {
        if (active is not { } tab) return;
        try
        {
            tab.ScrollOffset = PreviewOffset;
            var file = await Task.Run(() => FileService.ReadAsync(tab.FilePath));
            tab.File = file;
            tab.Parsed = null;
            if (active == tab)
            {
                UpdateDocumentInfo();
                await RenderAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            ShowError(ex);
        }
    }

    /// <summary>
    /// 关闭某个标签上的小叉号。
    /// </summary>
    private void OnCloseSpecificTab(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        CloseTab((sender as FrameworkElement)?.DataContext as DocumentTab);
    }

    /// <summary>
    /// 拖放文件到窗口时的入口。
    /// </summary>
    private void OnDrop(object sender, DragEventArgs e) => _ = DropFilesAsync(e);

    /// <summary>
    /// 从拖放数据里取出文件路径并打开。
    /// </summary>
    private async Task DropFilesAsync(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var path in paths.Where(File.Exists)) await OpenFileAsync(path);
    }

    /// <summary>
    /// 窗口关闭前保存偏好，并取消仍在进行的渲染。
    /// </summary>
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        try
        {
            preferences.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(ex);
        }

        renderCancellation?.Cancel();
    }

    /// <summary>
    /// 用 WPF 的消息框显示错误。
    /// </summary>
    private void ShowError(Exception ex)
    {
        MessageBox.Show(this, ex.Message, UiText.Get("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>
    /// 调整阅读字号，范围限制在 10 到 28。
    /// </summary>
    private void Zoom(int direction)
    {
        preferences.FontSize = Math.Clamp(preferences.FontSize + direction, 10, 28);
        ApplyReadingSize();
    }

    /// <summary>
    /// 把当前字号同步给阅读控件。
    /// </summary>
    private void ApplyReadingSize()
    {
        preview?.ReadingFontSize = Math.Clamp(preferences.FontSize, 10, 28);
    }

    /// <summary>
    /// 按住 Ctrl 滚动滚轮时缩放字号。
    /// </summary>
    private void OnZoomWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        Zoom(Math.Sign(e.Delta));
        e.Handled = true;
    }

    /// <summary>
    /// 根据当前主题切换标题栏按钮的图标和提示文字。
    /// </summary>
    private void UpdateThemeButton(bool animate)
    {
        Motion.SwapIcon(ThemeButton, FindResource(preferences.Dark ? "Icon.Sun" : "Icon.Theme"), animate);
        ThemeButton.SetResourceReference(ToolTipProperty, preferences.Dark ? "L.ToLight" : "L.ToDark");
    }

    /// <summary>
    /// 切换浅色/深色主题，并让阅读器重新取色。
    /// </summary>
    private void OnTheme(object sender, RoutedEventArgs e)
    {
        preferences.Dark = !preferences.Dark;
        ThemeService.Apply(preferences.Dark, true);
        UpdateThemeButton(true);

        // 阅读器里的画刷是创建时从资源里取的，换主题后要重建一次才能拿到新颜色。
        RefreshPreview();
    }

    /// <summary>
    /// 重建阅读器，让已经缓存的画刷按新主题重新解析。
    /// </summary>
    private void RefreshPreview()
    {
        active?.ScrollOffset = PreviewOffset;
        PreviewHost.Child = null;
        preview = null;
        if (active?.Parsed is { } result) ApplyParsed(active, result);
    }

    /// <summary>
    /// 加载并应用自定义背景图；没有自定义图时使用内置星空图。
    /// </summary>
    private void ApplyBackground()
    {
        Root.SetResourceReference(Panel.BackgroundProperty, preferences.Background ? "BackgroundBase" : "Surface");
        Wallpaper.Visibility = preferences.Background ? Visibility.Visible : Visibility.Collapsed;
        Wallpaper.Opacity = 1 - Math.Clamp(preferences.BackgroundTransparency, 0, 100) / 100;

        if (!preferences.Background || Wallpaper.Source is not null) return;
        try
        {
            // Window 的背景图用 Image 控件显示。BitmapImage 的 BeginInit/EndInit
            // 之间设置属性，EndInit 时才解码；DecodePixelWidth 限制解码宽度省内存。
            var image = new BitmapImage();
            image.BeginInit();
            image.DecodePixelWidth = 1920;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = preferences.CustomBackground is { } path && File.Exists(path)
                ? new Uri(path)
                : new Uri("pack://application:,,,/MokiPad;component/Assets/starlight.png");
            image.EndInit();
            image.Freeze();
            Wallpaper.Source = image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            Status.Text = ex.Message;
        }
    }

    /// <summary>
    /// 切换窗口置顶。
    /// </summary>
    private void OnPin(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinButton.SetResourceReference(ForegroundProperty, Topmost ? "Accent" : "Text");
    }

    /// <summary>
    /// 最小化窗口（自定义标题栏按钮）。
    /// </summary>
    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// 在最大化和普通大小之间切换。
    /// </summary>
    private void OnMaximize(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    /// <summary>
    /// 关闭窗口。
    /// </summary>
    private void OnExit(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 创建一个上下文菜单，并保证同一时间只打开一个。
    /// </summary>
    private ContextMenu NewMenu(UIElement? target = null)
    {
        var menu = new ContextMenu { PlacementTarget = target };
        menu.Opened += (_, _) =>
        {
            if (openMenu is not null && openMenu != menu) openMenu.IsOpen = false;
            openMenu = menu;
        };
        menu.Closed += (_, _) =>
        {
            if (openMenu == menu) openMenu = null;
        };
        return menu;
    }

    /// <summary>
    /// 创建一个菜单项；标题用资源键，支持快捷键提示和勾选状态。
    /// </summary>
    private static MenuItem Item(ContextMenu menu, string key, Action action, string? gesture = null,
        bool? check = null)
    {
        var item = new MenuItem
            { InputGestureText = gesture ?? "", IsCheckable = check.HasValue, IsChecked = check == true };
        item.SetResourceReference(HeaderedItemsControl.HeaderProperty, "L." + key);
        item.Click += (_, _) => action();
        menu.Items.Add(item);
        return item;
    }

    /// <summary>
    /// 在指定控件旁边打开菜单。
    /// </summary>
    private void ShowMenu(ContextMenu menu, UIElement? target)
    {
        openMenu?.IsOpen = false;
        menu.PlacementTarget = target;
        menu.IsOpen = true;
    }

    /// <summary>
    /// 文件菜单：打开、重新加载、关闭、设置、退出。
    /// </summary>
    private ContextMenu CreateFileMenu()
    {
        var menu = NewMenu(FileMenuButton);
        Item(menu, "Open", () => OnOpen(this, new RoutedEventArgs()), "Ctrl+O");
        Item(menu, "Reload", () => _ = ReloadAsync(), "F5").IsEnabled = active is not null;
        Item(menu, "Close", () => CloseTab(active), "Ctrl+W").IsEnabled = active is not null;
        menu.Items.Add(new Separator());
        Item(menu, "Settings", () => OnSettings(FileMenuButton, new RoutedEventArgs()));
        menu.Items.Add(new Separator());
        Item(menu, "Exit", Close);
        return menu;
    }

    /// <summary>
    /// 打开文件菜单。
    /// </summary>
    private void OnFileMenu(object sender, RoutedEventArgs e)
    {
        ShowMenu(CreateFileMenu(), FileMenuButton);
    }

    /// <summary>
    /// 设置菜单：背景、缩放、语言、关于。
    /// </summary>
    private ContextMenu CreateSettingsMenu()
    {
        var menu = NewMenu();
        Item(menu, "Background", () =>
        {
            preferences.Background = !preferences.Background;
            ApplyBackground();
        }, check: preferences.Background);
        Item(menu, "ChooseBackground", () =>
        {
            var dialog = new OpenFileDialog { Filter = UiText.Get("ImageFilter") };
            if (dialog.ShowDialog(this) != true) return;
            preferences.CustomBackground = dialog.FileName;
            preferences.Background = true;
            Wallpaper.Source = null;
            ApplyBackground();
        });
        Item(menu, "ResetBackground", () =>
        {
            preferences.CustomBackground = null;
            preferences.Background = true;
            Wallpaper.Source = null;
            ApplyBackground();
        });
        menu.Items.Add(new Separator());
        Item(menu, "ZoomIn", () => Zoom(1), "Ctrl++");
        Item(menu, "ZoomOut", () => Zoom(-1), "Ctrl+-");
        menu.Items.Add(new Separator());
        Item(menu, "Language", () =>
        {
            preferences.Language = preferences.Language == "zh" ? "en" : "zh";
            UiText.Apply(preferences.Language);
            UpdateDocumentInfo();
            RefreshPreview();
        });
        Item(menu, "AboutMenu", () => MessageBox.Show(this, UiText.Get("ReaderAbout"), "MokiPad"));
        return menu;
    }

    /// <summary>
    /// 打开设置菜单。
    /// </summary>
    private void OnSettings(object sender, RoutedEventArgs e)
    {
        ShowMenu(CreateSettingsMenu(), sender as UIElement);
    }

    /// <summary>
    /// 阅读区右键菜单：复制当前块或整篇文档。
    /// </summary>
    private ContextMenu CreateReaderMenu(PreviewBlock? block = null)
    {
        var menu = NewMenu(preview);
        Item(menu, "CopySelectedMarkdown", () => preview?.CopySelection(), "Ctrl+C").IsEnabled = preview?.HasMarkdownSelection == true;
        Item(menu, "CopyBlockMarkdown", () => { if (block is not null) preview?.CopyBlock(block); }).IsEnabled = block is not null;
        Item(menu, "SelectAll", () => preview?.SelectAllMarkdown(), "Ctrl+A").IsEnabled = active is not null;
        Item(menu, "CopyDocument", () =>
        {
            if (active is not null && !MarkdownClipboard.Write(active.File.Text)) Status.Text = UiText.Get("ClipboardBusy");
        }).IsEnabled = active is not null;
        menu.Items.Add(new Separator());
        Item(menu, "Reload", () => _ = ReloadAsync(), "F5").IsEnabled = active is not null;
        return menu;
    }

    /// <summary>
    /// 阅读区右键时找到鼠标下的块，再创建对应菜单。
    /// </summary>
    private void OnReaderContextMenu(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        var block = (e.OriginalSource as DependencyObject)?.FindParent<BlockPresenter>()?.Block;
        ShowMenu(CreateReaderMenu(block), preview);
    }

    /// <summary>
    /// 标签栏右键菜单：关闭当前、关闭其他、关闭全部。
    /// </summary>
    private void OnTabContextMenu(object sender, ContextMenuEventArgs e)
    {
        var tab = TabUnder(e.OriginalSource as DependencyObject) ?? active;
        if (tab is null) return;
        e.Handled = true;
        var menu = NewMenu(Tabs);
        Item(menu, "Close", () => CloseTab(tab), "Ctrl+W");
        Item(menu, "CloseOthers", () =>
        {
            foreach (var other in documents.Where(d => d != tab).ToArray()) documents.Remove(other);
            Tabs.SelectedItem = tab;
        });
        Item(menu, "CloseAll", () =>
        {
            documents.Clear();
            ShowWelcome();
        });
        ShowMenu(menu, Tabs);
    }

    /// <summary>
    /// 从鼠标下的可视元素往上找，找到它所属的标签数据。
    /// </summary>
    private DocumentTab? TabUnder(DependencyObject? source)
    {
        return source is null
            ? null
            : (ItemsControl.ContainerFromElement(Tabs, source) as ListBoxItem)?.DataContext as DocumentTab;
    }

    /// <summary>
    /// 鼠标中键点击标签时关闭它。
    /// </summary>
    private void OnTabMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle) return;
        e.Handled = true;
        CloseTab(TabUnder(e.OriginalSource as DependencyObject));
    }

    /// <summary>
    /// 标签栏宽度变化时，限制标签列表的最大宽度。
    /// </summary>
    private void OnTabStripSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Tabs.MaxWidth = Math.Max(80, TabStrip.ActualWidth - 40);
    }

    /// <summary>
    /// 在标签栏上滚动滚轮时横向滚动标签。
    /// </summary>
    private void OnTabWheel(object sender, MouseWheelEventArgs e)
    {
        if (FindChild<ScrollViewer>(Tabs) is not { } scroll) return;
        scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    /// <summary>
    /// 在 WPF 可视树里递归查找指定类型的子元素。
    /// </summary>
    private static T? FindChild<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }

        return null;
    }

    /// <summary>
    /// 以下方法只给离屏验证程序使用，生产界面不会调用。
    /// </summary>
    /// <summary>验证用：滚动到指定块。</summary>
    public void NavigateVerificationBlock(int index)
    {
        preview?.Navigate(index);
    }

    /// <summary>
    /// 按源码行号定位到第一个不早于该行的块。
    /// </summary>
    public void VerificationNavigateLine(int line)
    {
        if (preview is null) return;
        for (var i = 0; i < preview.Items.Count; i++)
            if (preview.Items[i] is PreviewBlock block && block.Line >= line)
            {
                preview.Navigate(i);
                return;
            }
    }

    /// <summary>
    /// 验证用：跳转到标题锚点。
    /// </summary>
    public void VerificationAnchor(string anchor)
    {
        preview?.ScrollToAnchor(anchor);
    }

    /// <summary>
    /// 验证用：创建指定种类的菜单。
    /// </summary>
    public ContextMenu VerificationMenu(string kind)
    {
        return kind switch { "file" => CreateFileMenu(), "settings" => CreateSettingsMenu(), _ => CreateReaderMenu() };
    }

    /// <summary>
    /// 验证用：强制切换主题。
    /// </summary>
    public void ApplyVerificationTheme(bool dark)
    {
        preferences.Dark = dark;
        ThemeService.Apply(dark);
        UpdateThemeButton(false);
        RefreshPreview();
    }

    /// <summary>
    /// 验证用：强制打开背景图。
    /// </summary>
    public void ApplyVerificationBackground()
    {
        preferences.Background = true;
        ApplyBackground();
    }

    /// <summary>
    /// 验证结束：取消渲染并解除关闭事件，避免影响后续测试。
    /// </summary>
    public void EndVerification()
    {
        renderCancellation?.Cancel();
        Closing -= OnClosing;
    }
}
