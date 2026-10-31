using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Input;
using System.Windows.Threading;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Syntax.Inlines;
using MokiPad.Core;
using MokiPad.Rendering;
using MokiPad.Services;
using Inline = System.Windows.Documents.Inline;

namespace MokiPad.Controls;

/// <summary>
/// 阅读区。它本身是一个 <see cref="ListBox"/>：Markdown 被切成一个个“块”，
/// 每个块是一个列表项；列表只创建屏幕附近的项目，所以长文也不会一次生成几万个控件。
/// </summary>
/// <remarks>
/// 这里用到几个 WPF 的关键概念：
/// <list type="bullet">
/// <item><description><b>虚拟化</b>：<see cref="VirtualizingStackPanel"/> 只实现可见区域附近的项，其余项暂时不创建。</description></item>
/// <item><description><b>容器</b>：ListBox 为每个数据项生成一个 <see cref="ListBoxItem"/>，里面再放真正的块控件。</description></item>
/// <item><description><b>依赖属性</b>：字号用附加依赖属性表示，可以沿可视树自动传给每个块。</description></item>
/// <item><description><b>Dispatcher</b>：所有界面操作都必须回到 UI 线程执行。</description></item>
/// </list>
/// </remarks>
public sealed partial class MarkdownPreview : ListBox
{
    /// <summary>
    /// 阅读字号。这是一个“附加依赖属性”，并且设置了 Inherits：
    /// 在阅读器上设置一次，内部所有 <see cref="BlockPresenter"/> 都能读到同一个值。
    /// 属性变化时会调用回调，让已经显示的块重新排版。
    /// </summary>
    public static readonly DependencyProperty ReadingFontSizeProperty = DependencyProperty.RegisterAttached(
        "ReadingFontSize", typeof(double), typeof(MarkdownPreview), new FrameworkPropertyMetadata(16.0,
            FrameworkPropertyMetadataOptions.Inherits,
            (obj, _) =>
            {
                if (obj is BlockPresenter block) block.Render();
            }));

    /// <summary>脚注序号到正文中引用块下标的映射，用于“从脚注跳回引用”。</summary>
    private readonly Dictionary<int, int> footnoteReferences = [];

    /// <summary>ListBox 内部真正负责滚动的控件，第一次滚动时才查找。</summary>
    private ScrollViewer? readingScroll;

    /// <summary>是否正在播放平滑滚动动画。</summary>
    private bool scrolling;

    /// <summary>上一次滚轮的方向：1 向上，-1 向下，0 没有滚动。</summary>
    private int wheelDirection;

    /// <summary>上一帧的时间戳，用来计算动画步长。</summary>
    private long lastScrollTick;

    // 按视口像素保存剩余距离。虚拟化重新估算总高度时可以修正绝对偏移；
    // 若继续追逐修正前的固定目标，动画就会把已经锚定好的内容反向拉回。
    private double remainingScroll;
    private double scrollRemainder;
    private TimeSpan lastRenderingTime = TimeSpan.MinValue;

    /// <summary>当前位置加上剩余滚动距离；随虚拟化的坐标修正一起移动。</summary>
    internal double ScrollTargetOffset => readingScroll is null ? 0 :
        Math.Clamp(readingScroll.VerticalOffset + remainingScroll + scrollRemainder, 0, readingScroll.ScrollableHeight);


    /// <summary>
    /// 构造阅读器，并用代码配置虚拟化列表的外观。
    /// 这些设置等价于在 XAML 里写附加属性，只是放在构造函数里更集中。
    /// </summary>
    public MarkdownPreview()
    {
        // CanContentScroll=true 表示由列表项面板自己管理滚动逻辑（虚拟化必须这样）。
        SetValue(ScrollViewer.CanContentScrollProperty, true);
        SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);

        // 开启虚拟化：只为可见区域附近的项创建控件。
        SetValue(VirtualizingPanel.IsVirtualizingProperty, true);

        // Recycling：滚出屏幕的容器会被回收，再用来显示新项，减少创建/销毁开销。
        SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);

        // Pixel：按像素滚动，而不是按“第几项”滚动，阅读长文更平滑。
        SetValue(VirtualizingPanel.ScrollUnitProperty, ScrollUnit.Pixel);

        // 在可见区域前后各缓存一页，减少快速滚动时的空白。
        SetValue(VirtualizingPanel.CacheLengthProperty, new VirtualizationCacheLength(1));

        // ItemsPanel 决定“项目如何排列”。这里用虚拟化堆叠面板，从上到下排列。
        ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));

        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;

        // ListBoxItem 的默认模板带选中高亮和边框，这里换成一个透明的 Border + ContentPresenter。
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(FocusableProperty, false));
        style.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));

        var template = new ControlTemplate(typeof(ListBoxItem));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty,
            new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        template.VisualTree = border;
        style.Setters.Add(new Setter(TemplateProperty, template));
        ItemContainerStyle = style;

        // ItemTemplate 决定“每个块长什么样”：这里放一个 BlockPresenter，
        // 并把它的 Block 属性绑定到当前数据项。
        var block = new FrameworkElementFactory(typeof(BlockPresenter));
        block.SetBinding(BlockPresenter.BlockProperty, new Binding());
        ItemTemplate = new DataTemplate { VisualTree = block };

        // PreviewMouseWheel 是“隧道”事件，会先从窗口往下传。
        // 这样即使鼠标在代码块内部，也能先由阅读器接住滚轮，而不是被内部滚动条吃掉。
        AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(OnReadingWheel), true);

        // 控件卸载时停止动画，避免 CompositionTarget.Rendering 一直持有它。
        Unloaded += (_, _) => StopScrolling();
        InitializeSelection();
        PreviewMouseDown += (_, _) => StopScrolling();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Space)
                StopScrolling();
        };
    }

    /// <summary>
    /// 处理滚轮。WPF 的滚轮事件是“隧道”事件，先经过父级再到达子级；
    /// 这里在父级接住它，统一控制整个文档的平滑滚动。
    /// </summary>
    private void OnReadingWheel(object sender, MouseWheelEventArgs e)
    {
        // Ctrl+滚轮用于缩放，交给窗口处理；Delta 为 0 不是有效滚动。
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 || e.Delta == 0) return;

        readingScroll ??= DescendantScroll(this);
        if (readingScroll is null) return;

        // Shift+滚轮：如果鼠标下有一个可横向滚动的代码块，就横向滚动它。
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 &&
            (e.OriginalSource as DependencyObject)?.FindParent<ScrollViewer>() is
                { ScrollableWidth: > 0 } horizontal &&
            horizontal != readingScroll)
        {
            horizontal.ScrollToHorizontalOffset(horizontal.HorizontalOffset - e.Delta);
            e.Handled = true;
            return;
        }

        // 标记事件已处理，防止内部代码块的 ScrollViewer 再滚一次。
        e.Handled = true;

        var direction = Math.Sign(e.Delta);

        if (!scrolling || direction != wheelDirection)
        {
            // 方向改变时，以当前位置为新起点，取消上一段动画还没走完的目标。
            // 这一步也会清掉 WPF 可能已经排队的旧滚动命令。
            remainingScroll = 0;
            scrollRemainder = 0;
            if (scrolling) readingScroll.ScrollToVerticalOffset(readingScroll.VerticalOffset);
        }

        wheelDirection = direction;

        // WheelScrollLines 是系统设置里“一次滚轮滚几行”。
        // 小于 0 表示“按页滚动”；否则把行数换算成像素。
        var lines = SystemParameters.WheelScrollLines;
        var distance = lines < 0
            ? readingScroll.ViewportHeight * .9
            : Math.Clamp(lines, 1, 8) * (double)GetValue(ReadingFontSizeProperty) * 1.75 * 1.25;

        // 向上滚是减小偏移，向下滚是增大偏移。
        remainingScroll = Math.Clamp(remainingScroll - e.Delta / 120.0 * distance,
            -readingScroll.VerticalOffset, Math.Max(0, readingScroll.ScrollableHeight - readingScroll.VerticalOffset));

        // 系统关闭动画时直接跳到目标，不做逐帧插值。
        if (!SystemParameters.ClientAreaAnimation)
        {
            readingScroll.ScrollToVerticalOffset(ScrollTargetOffset);
            remainingScroll = 0;
            return;
        }

        if (scrolling) return;

        // CompositionTarget.Rendering 会在每一帧渲染前触发，用它驱动平滑动画，
        // 比 DispatcherTimer 更贴合屏幕刷新节奏。
        scrolling = true;
        lastScrollTick = Stopwatch.GetTimestamp();
        lastRenderingTime = TimeSpan.MinValue;
        CompositionTarget.Rendering += AdvanceScroll;
    }

    /// <summary>
    /// 每帧消耗一部分剩余距离。布局重定位绝对坐标时，剩余距离和运动方向保持不变。
    /// </summary>
    private void AdvanceScroll(object? sender, EventArgs e)
    {
        // WPF can raise Rendering again after layout changes within the same frame.
        // Consume a frame's distance only once.
        if (e is RenderingEventArgs rendering)
        {
            if (rendering.RenderingTime == lastRenderingTime) return;
            lastRenderingTime = rendering.RenderingTime;
        }
        if (readingScroll is null)
        {
            StopScrolling();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        // A delayed frame must not make up the entire pause in a single visible jump.
        var seconds = Math.Clamp(Stopwatch.GetElapsedTime(lastScrollTick, now).TotalSeconds, 0, 1.0 / 60);
        lastScrollTick = now;

        remainingScroll = Math.Clamp(remainingScroll, -readingScroll.VerticalOffset,
            Math.Max(0, readingScroll.ScrollableHeight - readingScroll.VerticalOffset));
        var difference = remainingScroll;

        // 指数平滑：每帧走剩余距离的一部分；0.045 秒是手感参数。
        var completing = Math.Abs(difference) < .5;
        var frameLimit = Math.Clamp(readingScroll.ViewportHeight * .12, 48, 96);
        var step = Math.Clamp(completing ? difference : difference * (1 - Math.Exp(-seconds / .045)), -frameLimit, frameLimit);
        remainingScroll -= step;
        // Accumulate subpixel motion instead of losing it to WPF's layout rounding
        // on every small step; this keeps the total wheel distance intact.
        scrollRemainder += step;
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        var pixels = scrollRemainder * scale;
        var applied = (completing ? Math.Round(pixels, MidpointRounding.AwayFromZero) : Math.Truncate(pixels)) / scale;
        scrollRemainder -= applied;
        if (applied != 0) readingScroll.ScrollToVerticalOffset(readingScroll.VerticalOffset + applied);

        // 足够接近就吸附到目标并结束动画。
        if (completing) StopScrolling();
    }

    /// <summary>测试用：立即到达动画目标并停止。</summary>
    internal void FinishScroll()
    {
        readingScroll?.ScrollToVerticalOffset(ScrollTargetOffset);
        StopScrolling();
    }

    /// <summary>停止逐帧动画，并从渲染事件上解绑。</summary>
    private void StopScrolling()
    {
        scrolling = false;
        remainingScroll = 0;
        scrollRemainder = 0;
        wheelDirection = 0;
        CompositionTarget.Rendering -= AdvanceScroll;
    }

    /// <summary>在可视树里递归查找内部的 ScrollViewer。</summary>
    private static ScrollViewer? DescendantScroll(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer found) return found;
            if (DescendantScroll(child) is { } nested) return nested;
        }

        return null;
    }

    /// <summary>
    /// 阅读字号。只写不读：读取统一用依赖属性，避免和 WPF 的属性系统绕开。
    /// </summary>
    public double ReadingFontSize
    {
        set => SetValue(ReadingFontSizeProperty, value);
    }

    /// <summary>
    /// 当前文档的完整路径，用来解析图片和链接的相对地址。
    /// </summary>
    public string? DocumentPath { get; set; }

    /// <summary>
    /// 当前真正创建了控件的块数量（虚拟化是否生效，看这个值）。
    /// </summary>
    public int RealizedCount => Enumerable.Range(0, Items.Count)
        .Count(i => ItemContainerGenerator.ContainerFromIndex(i) is not null);

    /// <summary>Raised when a relative or absolute local link should open in another tab.</summary>
    /// <summary>
    /// 用户点击了本地链接，需要由窗口打开另一个标签。
    /// </summary>
    public event Action<string>? OpenFileRequested;

    /// <summary>
    /// 触发“打开本地文件”事件。
    /// </summary>
    internal void RequestOpenFile(string path)
    {
        OpenFileRequested?.Invoke(path);
    }

    /// <summary>
    /// 换一整篇文档。设置 ItemsSource 后，ListBox 会自动生成可见的块。
    /// </summary>
    public void SetBlocks(IReadOnlyList<PreviewBlock> blocks, string source = "")
    {
        StopScrolling(); footnoteReferences.Clear();
        EndDrag(); ClearMarkdownSelection(); sourceBlocks = blocks; documentSource = source;
        ItemsSource = blocks;
    }

    /// <summary>
    /// 滚动到指定块。ScrollIntoView 是 ListBox 自带的方法，会考虑虚拟化。
    /// </summary>
    public void Navigate(int index)
    {
        StopScrolling();
        if (index >= 0 && index < Items.Count && Items[index] is { } item) ScrollIntoView(item);
    }

    /// <summary>
    /// 记录脚注序号与正文引用块的位置关系。
    /// </summary>
    internal void MapFootnote(int order, int blockIndex)
    {
        if (order >= 0 && blockIndex >= 0) footnoteReferences.TryAdd(order, blockIndex);
    }

    /// <summary>
    /// 按标题锚点滚动；锚点已经是规范化后的 slug。
    /// </summary>
    public void ScrollToAnchor(string anchor)
    {
        var slug = MarkdownEngine.Slugify(Uri.UnescapeDataString(anchor.TrimStart('#')));
        for (var i = 0; i < Items.Count; i++)
            if (Items[i] is PreviewBlock { Anchor.Length: > 0 } block &&
                string.Equals(block.Anchor, slug, StringComparison.OrdinalIgnoreCase))
            {
                ScrollToIndex(i);
                return;
            }
    }

    /// <summary>
    /// 按脚注序号滚动到脚注定义。
    /// </summary>
    public void ScrollToFootnote(int order)
    {
        for (var i = 0; i < Items.Count; i++)
            if (Items[i] is PreviewBlock { Kind: "footnote" } block && block.FootnoteOrder == order)
            {
                ScrollToIndex(i);
                return;
            }
    }

    /// <summary>
    /// 从脚注跳回正文里的引用位置。
    /// </summary>
    internal void ScrollToFootnoteReference(int order)
    {
        if (footnoteReferences.TryGetValue(order, out var index)) ScrollToIndex(index);
    }

    /// <summary>
    /// 滚动到某一块，并给它一个短暂的强调底色。
    /// </summary>
    private void ScrollToIndex(int index)
    {
        StopScrolling();
        if (index < 0 || index >= Items.Count || Items[index] is not { } item) return;
        ScrollIntoView(item);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) return;
            var accent = WpfApp.Current.FindResource("Accent") as SolidColorBrush ?? Brushes.Transparent;
            container.Background =
                new SolidColorBrush(Color.FromArgb(46, accent.Color.R, accent.Color.G, accent.Color.B));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                container.Background = Brushes.Transparent;
            };
            timer.Start();
        }));
    }

    /// <summary>
    /// 把 Markdown 里的相对链接解析成本地绝对路径；网络链接返回 null。
    /// </summary>
    internal string? ResolvePath(string url)
    {
        var value = Uri.UnescapeDataString(url.Split('#')[0].Trim());
        if (value.Length == 0 || DocumentPath is null) return null;
        try
        {
            return Path.GetFullPath(Path.IsPathRooted(value)
                ? value
                : Path.Combine(Path.GetDirectoryName(DocumentPath) ?? "", value));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

/// <summary>
/// 对 <see cref="Application.Current"/> 的一层包装：WPF 的资源（画刷、样式、图标）
/// 都挂在 Application 上；把它集中在这里，资源缺失时能给出明确异常。
/// </summary>
internal static class WpfApp
{
    public static Application Current => Application.Current ??
                                         throw new InvalidOperationException(
                                             "MokiPad requires a WPF application context.");
}

/// <summary>
/// 在可视树/逻辑树里向上查找父元素的扩展方法。
/// WPF 元素有两种“父”：可视父（VisualTreeHelper）和逻辑父（LogicalTreeHelper），
/// 模板内部通常要用可视树，ContentControl 的逻辑结构则要用逻辑树。
/// </summary>
internal static class Parents
{
    /// <summary>从当前节点向上找第一个类型为 <typeparamref name="T"/> 的父元素。</summary>
    public static T? FindParent<T>(this DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;

            // Visual3D 不在普通的 Visual 树里，需要单独判断。
            node = node is Visual or Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }

        return null;
    }
}

/// <summary>
/// 一个 Markdown 块的显示控件。它继承 <see cref="ContentControl"/>，
/// 内部根据块的类型生成标题、段落、代码块、表格、公式等 WPF 元素。
/// </summary>
/// <remarks>
/// 列表虚拟化会复用这个控件：同一个实例可能先后显示不同的块。
/// 因此 <see cref="Render"/> 每次都会重新构建 Content，不能假设控件只显示一个块。
/// </remarks>
public sealed class BlockPresenter : ContentControl
{
    /// <summary>
    /// 当前显示的块。依赖属性（DependencyProperty）让 WPF 的绑定、样式和属性继承都能识别它；
    /// 值变化时由 <see cref="OnBlock"/> 触发重新渲染。
    /// </summary>
    public static readonly DependencyProperty BlockProperty = DependencyProperty.Register(nameof(Block),
        typeof(PreviewBlock), typeof(BlockPresenter), new PropertyMetadata(null, OnBlock));

    /// <summary>图片在表格/段落中可占的宽度比例，渲染图片时会动态调整。</summary>
    private double imageShare = 1;

    /// <summary>创建一个空的块控件；真正内容在 <see cref="Render"/> 里生成。</summary>
    public BlockPresenter()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }

    /// <summary>当前数据块。</summary>
    public PreviewBlock? Block
    {
        get => (PreviewBlock?)GetValue(BlockProperty);
        set => SetValue(BlockProperty, value);
    }

    /// <summary>相对基准字号（16）的缩放比例，用于标题、代码行高。</summary>
    private double ReadingScale => (double)GetValue(MarkdownPreview.ReadingFontSizeProperty) / 16;

    /// <summary>从应用资源里取画刷；取不到时退回透明，避免空引用。</summary>
    private static Brush Brush(string key)
    {
        return WpfApp.Current.FindResource(key) as Brush ?? Brushes.Transparent;
    }

    /// <summary>依赖属性变化回调：切换数据块时重新构建内容。</summary>
    private static void OnBlock(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((BlockPresenter)d).Render();
    }

    /// <summary>
    /// 根据块的类型构建 WPF 内容。虚拟化会复用控件，所以每次都必须从头构建。
    /// </summary>
    internal void Render()
    {
        if (Block is not { } block)
        {
            Content = null;
            return;
        }

        Margin = new Thickness(32 + Math.Min(block.Level, 8) * (block.Kind == "paragraph" ? 15 : 0), 0, 32,
            block.Checked.HasValue || block.Prefix.Length > 0 ? 4 : 16);
        ToolTip = null;
        switch (block.Kind)
        {
            case "heading":
                Margin = new Thickness(32, block.Line == 1 ? 0 : 20, 32, 12);
                var heading = Text("", block.Level switch { 1 => 33.6, 2 => 24.8, 3 => 20.8, 4 => 17.92, _ => 16 });
                if (block.Inline is not null) Inline(block.Inline, heading.Inlines, heading.FontSize);
                else heading.Text = block.Text;
                heading.FontWeight = FontWeights.SemiBold;
                heading.LineHeight = heading.FontSize * 1.5;
                if (block.Level >= 5) heading.Foreground = Brush("Secondary");
                var row = new DockPanel();
                switch (block.Level)
                {
                    case 1:
                        row.HorizontalAlignment = HorizontalAlignment.Center;
                        var notebookPath = new System.Windows.Shapes.Path
                        {
                            Data = Geometry.Parse("M10,6 C10,4.89543 10.89543,4 12,4 L40,4 C41.1046,4 42,4.89543 42,6 L42,42 C42,43.1046 41.1046,44 40,44 L12,44 C10.89543,44 10,43.1046 10,42 Z M34,6 L34,42 M6,14 L14,14 M6,24 L14,24 M6,34 L14,34 M27,4 L39,4 M27,44 L39,44"),
                            Stroke = Brush("Text"), StrokeThickness = 4, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
                        };
                        var canvas = new Canvas { Width = 48, Height = 48 }; canvas.Children.Add(notebookPath);
                        var notebook = new Viewbox { Width = heading.FontSize * .85, Height = heading.FontSize * .7, Child = canvas, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                        DockPanel.SetDock(notebook, Dock.Left);
                        row.Children.Add(notebook);
                        break;
                    case 2:
                        var hash = Text("#", heading.FontSize);
                        hash.LineHeight = heading.LineHeight;
                        hash.Foreground = Brush("MdHash");
                        hash.FontWeight = FontWeights.SemiBold;
                        hash.Margin = new Thickness(0, 0, 6, 0);
                        DockPanel.SetDock(hash, Dock.Left);
                        row.Children.Add(hash);
                        break;
                }

                row.Children.Add(heading);
                Content = row;
                break;
            case "rule":
                Content = new Border
                    { Height = 1, Background = Brush("MdLine"), Margin = new Thickness(0, 12, 0, 12) }; break;
            case "math":
                Content = new MathView
                {
                    Source = block.Text, Display = true, MathFontSize = 17 * ReadingScale,
                    MathForeground = Brush("Text"), HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 10)
                };
                break;
            case "code":
                var panel = new DockPanel();
                var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
                var copy = new Button
                {
                    Style = WpfApp.Current.FindResource("IconButton") as Style ??
                            throw new InvalidOperationException("IconButton style is missing"),
                    Content = Application.Current?.FindResource("Icon.Copy"), ToolTip = UiText.Get("CopyCodeMarkdown"),
                    FontSize = 14, Width = 26, Height = 24, Padding = new Thickness(5)
                };
                copy.Click += (_, _) => this.FindParent<MarkdownPreview>()?.CopyBlock(block);
                DockPanel.SetDock(copy, Dock.Right);
                bar.Children.Add(copy);
                var label = Text(block.Language, 12);
                label.Foreground = Brush("Secondary");
                label.VerticalAlignment = VerticalAlignment.Center;
                bar.Children.Add(label);
                DockPanel.SetDock(bar, Dock.Top);
                panel.Children.Add(bar);
                var code = Text("", 14);
                code.FontFamily = AppFonts.Code;
                code.LineHeight = 21 * ReadingScale;
                code.TextWrapping = TextWrapping.NoWrap;
                code.Foreground = Brush("SyntaxText");
                ColorCode(block.Text.TrimEnd('\r', '\n'), block.Language, code.Inlines);
                panel.Children.Add(new ScrollViewer
                {
                    Content = code, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0)
                });
                Content = new Border
                {
                    Background = Brush("MdCode"), BorderBrush = Brush("MdLine"), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 12), Child = panel
                };
                break;
            case "html":
                Content = HtmlBlock(block.Text);
                break;
            case "footnote":
            case "footnote-cont":
                var body = Text("", 15);
                if (block.Inline is not null) Inline(block.Inline, body.Inlines, body.FontSize);
                else body.Text = block.Text;
                var note = new DockPanel
                    { Margin = block.Kind == "footnote-cont" ? new Thickness(24, 0, 0, 2) : new Thickness(0, 0, 0, 6) };
                if (block.Kind == "footnote")
                {
                    var number = Text(block.FootnoteOrder + ".", 13);
                    number.Foreground = Brush("Accent");
                    number.Margin = new Thickness(0, 1, 8, 0);
                    number.VerticalAlignment = VerticalAlignment.Top;
                    DockPanel.SetDock(number, Dock.Left);
                    note.Children.Add(number);
                    var back = new Hyperlink(new Run("↩"))
                    {
                        Foreground = Brush("Accent"), FontSize = 13 * ReadingScale, TextDecorations = null,
                        ToolTip = UiText.Get("BackToReference")
                    };
                    back.Click += (_, _) =>
                        this.FindParent<MarkdownPreview>()?.ScrollToFootnoteReference(block.FootnoteOrder);
                    var backHost = Text("", 13);
                    backHost.Inlines.Add(back);
                    backHost.Margin = new Thickness(8, 2, 0, 0);
                    backHost.VerticalAlignment = VerticalAlignment.Top;
                    DockPanel.SetDock(backHost, Dock.Right);
                    note.Children.Add(backHost);
                }

                note.Children.Add(body);
                Content = note;
                if (block.Kind == "footnote") Margin = new Thickness(32, 0, 32, 2);
                break;
            case "table": Content = Table(block); break;
            default:
                var text = Text("", 16);
                if (block.Inline is not null) Inline(block.Inline, text.Inlines, text.FontSize);
                else
                {
                    text.Text = block.Text;
                    if (!MarkdownCopy.IsAtomic(block) && text.Inlines.FirstInline is Run run)
                        MarkdownSource.Bind(run, block.CopyStart, block.CopyEnd);
                }
                var container = new DockPanel();
                if (block.Checked is { } check)
                {
                    if (check)
                    {
                        text.TextDecorations = TextDecorations.Strikethrough;
                        text.Foreground = Brush("Secondary");
                    }

                    var box = new CheckBox
                    {
                        IsChecked = check, IsHitTestVisible = false, Focusable = false,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, Math.Max(0, (text.LineHeight - 14) / 2), 8, 0)
                    };
                    DockPanel.SetDock(box, Dock.Left);
                    container.Children.Add(box);
                }
                else if (block.Prefix.Length > 0)
                {
                    var bullet = Text(block.Prefix, 16);
                    bullet.Foreground = Brush("Accent");
                    bullet.Margin = new Thickness(0, 0, 10, 0);
                    DockPanel.SetDock(bullet, Dock.Left);
                    container.Children.Add(bullet);
                }

                container.Children.Add(text);
                Content = block.Kind == "quote"
                    ? new Border
                    {
                        Background = Brush("MdQuote"), CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(12, 8, 12, 8), Child = container
                    }
                    : container;
                break;
        }
    }

    /// <summary>
    /// 创建一个统一样式的文本控件。<see cref="TextBlock"/> 适合显示只读文字，
    /// 它的 <c>Inlines</c> 集合可以放加粗、链接、行内代码等富文本片段。
    /// </summary>
    private TextBlock Text(string text, double size)
    {
        return new TextBlock
        {
            Text = text, FontSize = size * ReadingScale, Foreground = Brush("Text"), TextWrapping = TextWrapping.Wrap,
            LineHeight = size * ReadingScale * 1.75, LineStackingStrategy = LineStackingStrategy.MaxHeight,
            FontFamily = AppFonts.Body, Cursor = Cursors.IBeam
        };
    }

    /// <summary>
    /// 把一段原始 HTML 转成一组 WPF 文本控件。
    /// </summary>
    private StackPanel HtmlBlock(string source)
    {
        var panel = new StackPanel();
        foreach (var piece in HtmlBlocks.Parse(source))
        {
            if (piece.Kind == "rule")
            {
                panel.Children.Add(new Border
                    { Height = 1, Background = Brush("MdLine"), Margin = new Thickness(0, 10, 0, 12) });
                continue;
            }

            var heading = piece.Kind is "h1" or "h2" or "h3" or "h4" or "h5" or "h6";
            var size = piece.Kind switch
            {
                "h1" => 33.6, "h2" => 24.8, "h3" => 20.8, "h4" => 17.92, "h5" => 16, "h6" => 16,
                "pre" => 14, _ => 16
            };
            var text = Text("", size);
            text.Margin = new Thickness(0, heading ? 14 : 0, 0, heading ? 8 : 10);
            text.TextAlignment = piece.Align switch
            {
                "center" => TextAlignment.Center,
                "right" => TextAlignment.Right,
                "justify" => TextAlignment.Justify,
                _ => TextAlignment.Left
            };
            if (heading)
            {
                text.FontWeight = FontWeights.SemiBold;
                text.LineHeight = text.FontSize * 1.5;
            }

            switch (piece.Kind)
            {
                case "pre":
                    text.FontFamily = AppFonts.Code;
                    break;
                case "li":
                    text.Margin = new Thickness(16, 0, 0, 2);
                    break;
            }
            HtmlContext.AppendFragment(piece.Html, text.Inlines, this, text.FontSize);
            if (text.Inlines.Count > 0) panel.Children.Add(text);
        }

        return panel;
    }

    /// <summary>
    /// 把表格数据排成 WPF 的 Grid：每个单元格是一个带边框的 Border。
    /// </summary>
    private Border Table(PreviewBlock block)
    {
        var rows = block.Rows ?? [];
        var cells = block.Cells;
        var align = block.ColumnAlign;
        var columns = cells?.Max(r => r.Length) ?? (rows.Length == 0 ? 0 : rows.Max(r => r.Length));
        var previousShare = imageShare;
        imageShare = 1.0 / Math.Max(1, columns);

        // Grid 是 WPF 的表格布局：ColumnDefinitions/RowDefinitions 定义行列，
        // 每个子元素用 Grid.SetRow/SetColumn 放到对应格子。
        var grid = new Grid();
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var r = 0; r < rows.Length; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < rows[r].Length; c++)
            {
                var text = Text("", 15);
                var inline = cells is not null && r < cells.Length && c < cells[r].Length ? cells[r][c] : null;
                if (inline is not null) Inline(inline, text.Inlines, text.FontSize);
                else text.Text = rows[r][c];
                if (r == 0)
                {
                    text.Foreground = Brush("Accent");
                    text.FontWeight = FontWeights.SemiBold;
                }

                text.TextAlignment = (align is not null && c < align.Length ? align[c] : "left") switch
                {
                    "center" => TextAlignment.Center,
                    "right" => TextAlignment.Right,
                    _ => TextAlignment.Left
                };
                var cell = new Border
                {
                    Child = text, Padding = new Thickness(12, 7, 12, 7), BorderThickness = new Thickness(0, 0, 1, 1),
                    BorderBrush = Brush("MdLine"),
                    Background = r == 0 ? Brush("MdTableHead") : r % 2 == 0 ? Brush("MdRowAlt") : Brushes.Transparent
                };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        imageShare = previousShare;

        // 外层 Border 负责圆角和整体边框，Grid 只负责单元格排列。
        return new Border
        {
            Child = grid, BorderThickness = new Thickness(1, 1, 0, 0), BorderBrush = Brush("MdLine"),
            CornerRadius = new CornerRadius(6), ClipToBounds = true
        };
    }

    /// <summary>
    /// 把 Markdig 的行内节点树（文字、强调、链接、代码、公式等）转换成 WPF 的 Inline 集合。
    /// </summary>
    private void Inline(ContainerInline container, InlineCollection output, double fontSize = 16)
    {
        // HtmlContext 同时负责转义文本、应用 HTML 标签和记录当前样式栈。
        var html = new HtmlContext(output) { FontSize = fontSize };
        for (var child = container.FirstChild; child is not null; child = child.NextSibling)
            switch (child)
            {
                case LiteralInline literal:
                    html.AddText(literal.Content.ToString(), literal.Span.Start, literal.Span.End + 1, false);
                    break;
                case HtmlEntityInline entity:
                    html.AddText(entity.Transcoded.ToString(), entity.Span.Start, entity.Span.End + 1, false);
                    break;
                case HtmlInline tag:
                    html.ApplyTag(tag.Tag, this, fontSize, tag.Span.Start, tag.Span.End + 1);
                    break;
                case CodeInline code:
                    var codeRun = new Run(code.Content)
                    {
                        FontFamily = AppFonts.Code,
                        FontSize = fontSize * .9,
                        Background = Brush("MdSoft"),
                        Foreground = Brush("Text")
                    };
                    MarkdownSource.Bind(codeRun, code.Span.Start, code.Span.End + 1, true);
                    html.Target.Add(codeRun);
                    break;
                case EmphasisInline emphasis:
                    var span = new Span();
                    switch (emphasis.DelimiterChar)
                    {
                        case '~':
                            span.TextDecorations = TextDecorations.Strikethrough;
                            break;
                        case '=':
                            span.Background = Brush("MdSoft");
                            break;
                        case '+':
                            span.TextDecorations = TextDecorations.Underline;
                            break;
                        case '^':
                            span.FontSize = fontSize * .75;
                            span.BaselineAlignment = BaselineAlignment.Superscript;
                            break;
                        default:
                            if (emphasis.DelimiterCount >= 2) span.FontWeight = FontWeights.Bold;
                            else span.FontStyle = FontStyles.Italic;
                            break;
                    }

                    Inline(emphasis, span.Inlines, fontSize);
                    html.Target.Add(span);
                    break;
                case MathInline math:
                    var view = new MathView
                    {
                        Source = math.Content.ToString(), Display = false, MathFontSize = fontSize,
                        MathForeground = Brush("Text"), VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(1, 0, 1, 0)
                    };
                    // InlineUIContainer 允许在文字流中嵌入一个完整的 WPF 控件（这里是公式）。
                    // BaselineAlignment 决定它和周围文字的垂直对齐方式。
                    var formula = new InlineUIContainer(new Border { Child = view, Background = Brushes.Transparent, Cursor = Cursors.Arrow }) { BaselineAlignment = BaselineAlignment.Center };
                    MarkdownSource.Bind(formula, math.Span.Start, math.Span.End + 1, true);
                    html.Target.Add(formula);
                    break;
                case FootnoteLink { IsBackLink: false } note:
                    var order = Math.Max(0, note.Footnote.Order);
                    var reference = new Hyperlink(new Run($"[{order}]"))
                    {
                        FontSize = fontSize * .72, BaselineAlignment = BaselineAlignment.Superscript,
                        Foreground = Brush("Accent"), TextDecorations = null, ToolTip = UiText.Get("Footnotes"),
                        Tag = order
                    };
                    reference.Click += (sender, _) =>
                    {
                        if (sender is Hyperlink { Tag: int targetOrder })
                            this.FindParent<MarkdownPreview>()?.ScrollToFootnote(targetOrder);
                    };
                    // Hyperlink 是 WPF 的行内链接；这里记录“脚注序号 -> 正文块下标”，
                    // 之后从脚注点回来时才能找到正确的引用位置。
                    var preview = this.FindParent<MarkdownPreview>();
                    if (preview is not null && Block is { } currentBlock)
                        preview.MapFootnote(order, preview.Items.IndexOf(currentBlock));
                    html.Target.Add(reference);
                    MarkdownSource.Bind(reference, note.Span.Start, note.Span.End + 1, true);
                    break;
                case FootnoteLink:
                    break;
                case LinkInline { IsImage: true } imageLink:
                    var image = ImageInline(imageLink.Url, MarkdownEngine.Plain(imageLink), fontSize);
                    if (image is not null) { MarkdownSource.Bind(image, imageLink.Span.Start, imageLink.Span.End + 1, true); html.Target.Add(image); }
                    break;
                case LinkInline anchorLink:
                    var hyperlink = CreateHyperlink(anchorLink.Url, fontSize);
                    Inline(anchorLink, hyperlink.Inlines, fontSize);
                    MarkdownSource.Bind(hyperlink, anchorLink.Span.Start, anchorLink.Span.End + 1, true);
                    html.Target.Add(hyperlink);
                    break;
                case AutolinkInline autoLink:
                    var auto = CreateHyperlink(autoLink.Url, fontSize);
                    auto.Inlines.Add(new Run(autoLink.Url));
                    MarkdownSource.Bind(auto, autoLink.Span.Start, autoLink.Span.End + 1, true);
                    html.Target.Add(auto);
                    break;
                case LineBreakInline line:
                    Inline lineView = line.IsHard || Block?.Kind == "quote" ? new LineBreak() : new Run(" ");
                    MarkdownSource.Bind(lineView, line.Span.Start, line.Span.End + 1); html.Target.Add(lineView); break;
                case ContainerInline nested: Inline(nested, html.Target, fontSize); break;
            }
    }

    /// <summary>
    /// 创建一个可点击的链接，并加上悬停下划线和点击跳转。
    /// </summary>
    internal Hyperlink CreateHyperlink(string? url, double fontSize)
    {
        var link = new Hyperlink
            { Foreground = Brush("Accent"), TextDecorations = null, ToolTip = url, FontSize = fontSize };
        link.MouseEnter += (_, _) => link.TextDecorations = TextDecorations.Underline;
        link.MouseLeave += (_, _) => link.TextDecorations = null;
        link.Click += (_, _) => ResolveLink(url);
        return link;
    }

    /// <summary>
    /// 处理链接点击：页内锚点、网页、本地文件分别走不同路径。
    /// </summary>
    private void ResolveLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        var preview = this.FindParent<MarkdownPreview>();
        if (url.StartsWith('#'))
        {
            preview?.ScrollToAnchor(url[1..]);
            return;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            switch (uri.Scheme)
            {
                case "http" or "https" or "mailto" or "tel":
                    try
                    {
                        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    }
                    catch (Win32Exception)
                    {
                    }

                    return;
                case "file":
                    preview?.RequestOpenFile(uri.LocalPath);
                    return;
            }
        }

        var path = preview?.ResolvePath(url);
        if (path is not null) preview?.RequestOpenFile(path);
    }

    /// <summary>
    /// 创建一个行内图片：先用占位文字，等后台加载完成后再显示真正的图片。
    /// GIF 会交给 GifAnimation 播放；普通图片按阅读区宽度自适应。
    /// </summary>
    internal Inline? ImageInline(string? source, string? alt, double fontSize, double? width = null,
        double? height = null)
    {
        if (string.IsNullOrWhiteSpace(source) && string.IsNullOrWhiteSpace(alt)) return null;
        var preview = this.FindParent<MarkdownPreview>();
        var caption = string.IsNullOrWhiteSpace(alt) ? "" : alt.Trim();
        var fallback = new TextBlock
        {
            Text = caption.Length > 0 ? caption : UiText.Get("Image"),
            FontSize = Math.Max(11, fontSize * .85), Foreground = Brush("Secondary"),
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
        };
        var image = new Image
        {
            Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
            MaxHeight = height is > 0 ? height.Value * ReadingScale : double.PositiveInfinity,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed, IsHitTestVisible = false
        };
        // 高质量缩放：图片被缩小时使用更好的插值，不会出现明显锯齿。
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        // MaxWidth 绑定到阅读区的实际宽度，再由转换器按比例和上限计算。
        var cap = width is > 0 ? width.Value * ReadingScale : double.PositiveInfinity;
        image.SetBinding(MaxWidthProperty, new Binding("ActualWidth")
        {
            Source = preview,
            Converter = new ReadingWidth(imageShare, cap)
        });
        // Grid 把占位文字和图片叠在一起：图片加载完成前显示文字，完成后隐藏文字。
        var content = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4), Background = Brushes.Transparent, Cursor = Cursors.Arrow };
        content.Children.Add(fallback);
        content.Children.Add(image);
        if (string.IsNullOrWhiteSpace(source)) return new InlineUIContainer(content) { BaselineAlignment = BaselineAlignment.Bottom };

        // Start from the dispatcher so images also load in offscreen verification runs,
        // where Loaded never fires for a window that is not shown.
        var url = source;
        image.Dispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(() => _ = LoadImageAsync(image, fallback, url, caption)));
        return new InlineUIContainer(content) { BaselineAlignment = BaselineAlignment.Bottom };
    }

    /// <summary>
    /// 后台加载图片或 GIF，并在 UI 线程替换占位内容。
    /// </summary>
    private async Task LoadImageAsync(Image target, TextBlock fallbackText, string url, string caption)
    {
        var preview = this.FindParent<MarkdownPreview>();
        var documentPath = preview?.DocumentPath;
        var bytes = await ImageLoader.ReadBytesAsync(url, documentPath);
        if (bytes is null) return;
        if (ImageLoader.IsGif(bytes))
        {
            // GIF 用专门的播放器：每一帧写进同一张 WriteableBitmap，不改变控件尺寸。
            var animation = new GifAnimation(target, bytes);
            target.Unloaded += (_, _) => GifAnimation.For(target)?.Dispose();
            try
            {
                await animation.StartAsync(); target.Tag = "gif"; target.Visibility = Visibility.Visible;
                fallbackText.Visibility = Visibility.Collapsed; if (caption.Length > 0) target.ToolTip = caption;
                return;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or ArgumentException or OperationCanceledException) { animation.Dispose(); }
        }

        var bitmap = await ImageLoader.LoadAsync(url, documentPath, RequestedPixels(target));
        if (bitmap is null) return;
        target.Source = bitmap;
        target.Visibility = Visibility.Visible;
        fallbackText.Visibility = Visibility.Collapsed;
        if (caption.Length > 0) target.ToolTip = caption;
        var upgrading = false;
        target.SizeChanged += async (_, _) =>
        {
            if (upgrading || !target.IsLoaded || target.Source is not BitmapSource source) return;
            var needed = RequestedPixels(target);
            if (needed <= source.PixelWidth * 1.25) return;
            upgrading = true;
            try { if (await ImageLoader.LoadAsync(url, documentPath, needed) is { } sharper) target.Source = sharper; }
            finally { upgrading = false; }
        };
    }
    /// <summary>
    /// 根据显示宽度和屏幕 DPI 算出需要多少物理像素，避免高 DPI 下图片发虚。
    /// </summary>
    private static int RequestedPixels(Image target)
    {
        var dipWidth = double.IsFinite(target.MaxWidth) && target.MaxWidth > 1 ? target.MaxWidth : 1280;
        return (int)Math.Ceiling(dipWidth * VisualTreeHelper.GetDpi(target).DpiScaleX);
    }

    private static void ColorCode(string source, string language, InlineCollection output)
    {
        // Bounded code chunks make lexical coloring cheap; no editor instance per code fence.
        var position = 0;
        foreach (var token in CodeSyntax.Tokenize(source, language))
        {
            if (token.Offset > position) output.Add(new Run(source[position..token.Offset]));
            output.Add(new Run(source.Substring(token.Offset, token.Length))
                { Foreground = Brush("Syntax" + token.Kind) });
            position = token.Offset + token.Length;
        }

        if (position < source.Length) output.Add(new Run(source[position..]));
    }

    /// <summary>
    /// 图片宽度转换器：根据阅读区宽度和当前块分到的比例，计算图片实际能占多宽。
    /// </summary>
    /// <summary>
    /// 图片宽度转换器：根据阅读区宽度和当前块分到的比例，计算图片实际能占多宽。
    /// WPF 的绑定可以把转换器串在“源属性 → 目标属性”之间，这里用来做自适应宽度。
    /// </summary>
    private sealed class ReadingWidth(double share = 1, double cap = double.PositiveInfinity) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Math.Min(cap, Math.Max(80, ((value as double? ?? 900) - 72) * Math.Clamp(share, .05, 1)));
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

/// <summary>
/// 把一段安全的 HTML 片段转换成 WPF 的 Inline 集合。
/// 支持常见行内标签（加粗、斜体、链接、代码、上下标等）和少量块级标签。
/// </summary>
internal sealed class HtmlContext(InlineCollection root)
{
    private static readonly Regex WhitespacePattern = new("\\s+", RegexOptions.Compiled);
    private static readonly Regex TagPattern = new("<[^>]*>", RegexOptions.Compiled);

    /// <summary>嵌套标签的样式栈；进入 &lt;b&gt; 时压栈，遇到 &lt;/b&gt; 时恢复。</summary>
    private readonly Stack<HtmlStyle> styles = new();

    /// <summary>链接嵌套时的目标集合栈，例如 &lt;a&gt;&lt;b&gt;文字&lt;/b&gt;&lt;/a&gt;。</summary>
    private readonly Stack<InlineCollection> targets = new();

    /// <summary>是否位于 &lt;script&gt;/&lt;style&gt; 内；这些内容不显示。</summary>
    private bool skip;

    /// <summary>当前行内样式。</summary>
    private HtmlStyle style = new();

    /// <summary>当前要追加到的 Inline 集合；进入链接时会临时切换。</summary>
    public InlineCollection Target { get; private set; } = root;

    /// <summary>当前文本字号，用于计算上下标和代码的相对大小。</summary>
    public double FontSize { get; init; } = 16;

    /// <summary>
    /// 追加普通文本，并应用当前样式。
    /// </summary>
    public void AddText(string text, int sourceStart = -1, int sourceEnd = -1, bool decode = true)
    {
        if (skip || text.Length == 0) return;
        var value = decode ? WebUtility.HtmlDecode(text) : text;
        if (value.Length == 0) return;
        var run = new Run(value);
        MarkdownSource.Bind(run, sourceStart, sourceEnd);
        if (style.Bold) run.FontWeight = FontWeights.Bold;
        if (style.Italic) run.FontStyle = FontStyles.Italic;
        if (style.Code || style.Kbd)
        {
            run.FontFamily = AppFonts.Code;
            run.Background = WpfApp.Current.FindResource("MdSoft") as Brush;
            run.FontSize = FontSize * .88;
        }

        if (style.Mark) run.Background = WpfApp.Current.FindResource("MdSoft") as Brush;
        if (style.Sup)
        {
            run.BaselineAlignment = BaselineAlignment.Superscript;
            run.FontSize *= .72;
        }

        if (style.Sub)
        {
            run.BaselineAlignment = BaselineAlignment.Subscript;
            run.FontSize *= .72;
        }

        if (style.Small) run.FontSize *= .86;
        if (style.Underline || style.Strike)
        {
            var decorations = new TextDecorationCollection();
            if (style.Underline) decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
            if (style.Strike) decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough });
            run.TextDecorations = decorations;
        }

        Target.Add(run);
    }

    /// <summary>
    /// 处理一个 HTML 标签：开始标签压栈，结束标签出栈。
    /// </summary>
    public void ApplyTag(string raw, BlockPresenter host, double fontSize, int sourceStart = -1, int sourceEnd = -1)
    {
        var tag = raw.Trim();
        if (tag.Length < 2 || tag.StartsWith("<!", StringComparison.Ordinal)) return;
        var closing = tag.StartsWith("</", StringComparison.Ordinal);
        var body = tag.Trim('<', '>', ' ').TrimStart('/');
        if (body.Length == 0) return;
        var name = body.Split(' ', '\t', '\r', '\n', '/')[0].ToLowerInvariant();
        if (skip && name is not ("script" or "style")) return;
        switch (name)
        {
            case "script":
            case "style":
                skip = !closing;
                return;
            case "br":
            case "hr":
                if (!closing) Target.Add(new LineBreak());
                return;
            case "img":
                if (closing) return;
                var src = Attribute(tag, "src");
                var alt = Attribute(tag, "alt");
                var width = Number(Attribute(tag, "width"));
                var height = Number(Attribute(tag, "height"));
                var image = host.ImageInline(src, alt, fontSize, width, height);
                if (image is not null) { MarkdownSource.Bind(image, sourceStart, sourceEnd, true); Target.Add(image); }
                return;
            case "a":
                if (closing)
                {
                    if (targets.Count > 0) Target = targets.Pop();
                    return;
                }

                var link = host.CreateHyperlink(Attribute(tag, "href"), fontSize);
                Target.Add(link);
                targets.Push(Target);
                Target = link.Inlines;
                return;
            case "sup":
                PushStyle(closing);
                style.Sup = !closing;
                return;
            case "sub":
                PushStyle(closing);
                style.Sub = !closing;
                return;
            case "code":
                PushStyle(closing);
                style.Code = !closing;
                return;
            case "kbd":
                PushStyle(closing);
                style.Kbd = !closing;
                return;
            case "mark":
                PushStyle(closing);
                style.Mark = !closing;
                return;
            case "small":
                PushStyle(closing);
                style.Small = !closing;
                return;
            case "u":
                PushStyle(closing);
                style.Underline = !closing;
                return;
            case "s":
            case "del":
            case "strike":
                PushStyle(closing);
                style.Strike = !closing;
                return;
            case "b":
            case "strong":
                PushStyle(closing);
                style.Bold = !closing;
                return;
            case "i":
            case "em":
                PushStyle(closing);
                style.Italic = !closing;
                return;
            case "p":
            case "div":
            case "section":
            case "details":
            case "summary":
            case "li":
            case "pre":
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                Target.Add(new LineBreak());
                return;
            default: return;
        }
    }

    /// <summary>
    /// 开始标签时复制当前样式，结束标签时恢复。
    /// </summary>
    private void PushStyle(bool closing)
    {
        if (!closing) styles.Push(style.Clone());
        else if (styles.Count > 0) style = styles.Pop();
    }

    /// <summary>
    /// 把一段 HTML 文本按标签切开，文本和标签分别交给 HtmlContext 处理。
    /// </summary>
    public static void AppendFragment(string html, InlineCollection output, BlockPresenter host, double fontSize)
    {
        var context = new HtmlContext(output) { FontSize = fontSize };
        var position = 0;
        foreach (Match match in TagPattern.Matches(html))
        {
            if (match.Index > position) context.AddText(WhitespacePattern.Replace(html[position..match.Index], " "));
            context.ApplyTag(match.Value, host, fontSize);
            position = match.Index + match.Length;
        }

        if (position < html.Length) context.AddText(WhitespacePattern.Replace(html[position..], " "));
    }

    /// <summary>
    /// 从标签字符串里读取属性值，例如 href、src、width。
    /// </summary>
    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, name + "\\s*=\\s*(\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return match.Groups[2].Success ? match.Groups[2].Value :
            match.Groups[3].Success ? match.Groups[3].Value : match.Groups[4].Value;
    }

    private static double? Number(string? value)
    {
        return double.TryParse(value, NumberStyles.Any,
            CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>
    /// 当前正在应用的 HTML 行内样式。用栈保存/恢复，支持嵌套标签。
    /// </summary>
    private sealed class HtmlStyle
    {
        public bool Bold, Italic, Underline, Strike, Code, Kbd, Mark, Sup, Sub, Small;

        public HtmlStyle Clone()
        {
            return (HtmlStyle)MemberwiseClone();
        }
    }

}
