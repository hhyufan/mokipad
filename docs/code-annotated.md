# MokiPad 逐行注释版代码导读

> 这份文档只做一件事：把每个功能模块的代码贴出来，**每一行后面都写注释**。  
> 先看第 1 节的文件架构和第 2 节的模块表，再按模块往下读。  
> 完整的文件说明和方法索引见 `docs/code-walkthrough.md`。

## 0. 怎么读

- 代码里的注释用 `//` 写在行尾，解释这一行在做什么。
- `...` 表示省略的同类代码。
- 每个模块先列涉及文件，再贴带注释的代码。
- 想运行项目，先看 `docs/beginner-guide.md` 的第 1 章。

---

## 1. 完整文件架构

```text
mokipad/
├─ MokiPad.sln                         解决方案文件
├─ MokiPad.slnx                        新版解决方案格式
├─ global.json                         .NET SDK 版本
├─ MokiPad.sln.DotSettings             ReSharper 命名规则
├─ .editorconfig                       代码风格
├─ .gitignore                          忽略构建产物
├─ LICENSE                             MIT 许可
├─ THIRD-PARTY-NOTICES.md              第三方许可
├─ README.md                           项目说明
├─ scripts/
│  ├─ dev.ps1                          启动开发版
│  ├─ publish.ps1                      发布
│  └─ verify.ps1                       离屏验证
├─ docs/
│  ├─ beginner-guide.md                语言基础
│  ├─ beginner-guide.mgtree            语言基础知识树
│  ├─ code-walkthrough.md              文件架构与模块导读
│  ├─ code-walkthrough.mgtree          导读知识树
│  ├─ code-annotated.md                本文（逐行注释）
│  ├─ code-annotated.mgtree            本文知识树
│  ├─ solution.html                    解决方案说明
│  ├─ code-quality-report.html         代码质量报告
│  ├─ code-cleanup-changes.md          清理记录
│  ├─ markdown-copy.md                 复制行为
│  ├─ scrolling-fix.md                 滚动修复
│  └─ images/                          截图
├─ src/
│  ├─ MokiPad.Core/
│  │  ├─ MokiPad.Core.csproj
│  │  ├─ MarkdownEngine.cs             Markdown 解析
│  │  ├─ FileService.cs                文件读取与编码
│  │  ├─ CodeSyntax.cs                 代码高亮
│  │  └─ MarkdownCopy.cs               选区转 Markdown
│  └─ MokiPad/
│     ├─ MokiPad.csproj
│     ├─ App.xaml                      全局资源
│     ├─ App.xaml.cs                   启动入口
│     ├─ GlobalUsings.cs               全局 using
│     ├─ MainWindow.xaml               主窗口布局
│     ├─ MainWindow.xaml.cs            窗口逻辑
│     ├─ Models/DocumentTab.cs         标签数据
│     ├─ Controls/
│     │  ├─ MarkdownPreview.cs         虚拟化阅读列表
│     │  ├─ MarkdownSelection.cs       选择与选区
│     │  ├─ HtmlBlocks.cs              块级 HTML
│     │  ├─ ImageLoader.cs             图片/SVG
│     │  ├─ BoundedImageCache.cs       有界缓存
│     │  └─ GifAnimation.cs            GIF 播放
│     ├─ Rendering/MathView.cs         公式宿主
│     ├─ Services/
│     │  ├─ Preferences.cs             本地设置
│     │  ├─ ThemeService.cs            主题
│     │  ├─ AppFonts.cs                字体
│     │  ├─ UiText.cs                  文案
│     │  ├─ Motion.cs                  动画
│     │  └─ MarkdownClipboard.cs       剪贴板
│     ├─ Styles/Controls.xaml          样式
│     ├─ Assets/                       图标、字体、许可
│     └─ Properties/AssemblyInfo.cs    程序集信息
└─ tests/MokiPad.Verification/
   ├─ MokiPad.Verification.csproj
   ├─ Program.cs                       验证入口
   ├─ MarkdownCopyVerification.cs      复制检查
   ├─ ScrollReplay.cs                  滚动回放
   └─ NativeScrollReplay.cs            原生滚动回放
```

---

## 2. 功能模块表

| 模块 | 文件 | 入口 |
| --- | --- | --- |
| 启动与窗口 | `App.xaml.cs`、`MainWindow.xaml.cs` | `App.OnStartup` |
| 文件读取 | `FileService.cs` | `ReadAsync` |
| Markdown 解析 | `MarkdownEngine.cs` | `Parse` |
| 阅读列表/滚动 | `MarkdownPreview.cs` | 构造函数、`OnReadingWheel` |
| 块渲染 | `MarkdownPreview.cs` | `BlockPresenter.Render` |
| HTML 行内 | `MarkdownPreview.cs`、`HtmlBlocks.cs` | `HtmlContext.ApplyTag` |
| 图片/SVG | `ImageLoader.cs`、`BoundedImageCache.cs` | `LoadAsync` |
| GIF | `GifAnimation.cs` | `StartAsync` |
| 数学 | `MathView.cs` | `Rebuild` |
| 选择复制 | `MarkdownSelection.cs`、`MarkdownCopy.cs` | `Selection` |
| 主题/字体 | `ThemeService.cs`、`AppFonts.cs` | `Apply` |
| 设置/文案 | `Preferences.cs`、`UiText.cs` | `Load`、`Get` |
| 菜单/快捷键 | `MainWindow.xaml.cs` | `BindKeys` |
| 验证 | `tests/MokiPad.Verification` | `Main` |

---

## 2.5 界面层：XAML 怎么画，怎么和 C# 对应

前面讲的是“功能代码”。这一节补上 **V（View）**：界面到底怎么用 XAML 画出来，XAML 里的名字、事件、绑定又是怎么落到 C# 的。

### 2.5.1 App.xaml：全局资源入口

```xml
<!-- 应用根元素；x:Class 指向 App.xaml.cs -->
<Application x:Class="MokiPad.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- 应用级资源：所有窗口都能用 -->
    <Application.Resources>
        <ResourceDictionary>

            <!-- 把 Styles/Controls.xaml 合并进来 -->
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Styles/Controls.xaml" />
            </ResourceDictionary.MergedDictionaries>

        </ResourceDictionary>
    </Application.Resources>
</Application>
```

| 行/属性 | 含义 |
| --- | --- |
| `<Application>` | 整个 WPF 应用的根；一个应用只有一个 |
| `x:Class="MokiPad.App"` | 这个 XAML 对应 `App.xaml.cs` 里的 `App` 类 |
| `xmlns` | WPF 控件命名空间；不写前缀的标签都从这里找 |
| `xmlns:x` | XAML 语言命名空间；`x:Class`、`x:Key` 来自这里 |
| `<Application.Resources>` | 应用级资源字典 |
| `MergedDictionaries` | 把多个资源文件合并成一个 |
| `Source="Styles/Controls.xaml"` | 引入全局样式、图标、控件模板 |

### 2.5.2 MainWindow.xaml：窗口骨架

```xml
<!-- 窗口根：x:Class 指向 MainWindow.xaml.cs -->
<Window x:Class="MokiPad.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:services="clr-namespace:MokiPad.Services"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        Title="MokiPad" Width="1280" Height="820"
        WindowStyle="None" ResizeMode="CanResize"
        Background="{DynamicResource Canvas}"
        AllowDrop="True" Drop="OnDrop" Closing="OnClosing">

    <!-- 自定义标题栏：去掉系统边框，但保留拖动/缩放/贴边 -->
    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="42" ResizeBorderThickness="6"
                            CornerRadius="10" GlassFrameThickness="0"
                            UseAeroCaptionButtons="False" />
    </shell:WindowChrome.WindowChrome>

    <!-- 根边框：圆角 + 主题边框色 -->
    <Border BorderBrush="{DynamicResource Border}" BorderThickness="1"
            CornerRadius="10" ClipToBounds="True">
        <Grid x:Name="Root" Background="{DynamicResource Surface}">

            <!-- 背景图：不参与命中测试，透明度 0.15 -->
            <Image x:Name="Wallpaper" Stretch="UniformToFill"
                   IsHitTestVisible="False" Opacity=".15" />

            <Grid>
                <!-- 四行：标题栏 42、标签栏 40、阅读区 *、状态栏 32 -->
                <Grid.RowDefinitions>
                    <RowDefinition Height="42" />
                    <RowDefinition Height="40" />
                    <RowDefinition Height="*" />
                    <RowDefinition Height="32" />
                </Grid.RowDefinitions>

                <!-- 第 0 行：标题栏 -->
                <Border BorderBrush="{DynamicResource Border}" BorderThickness="0,0,0,1">
                    <Grid Margin="6,0,10,0">

                        <!-- 左侧：文件菜单、打开按钮；必须能接收点击 -->
                        <StackPanel Orientation="Horizontal" HorizontalAlignment="Left"
                                    VerticalAlignment="Center"
                                    shell:WindowChrome.IsHitTestVisibleInChrome="True">
                            <Button x:Name="FileMenuButton" Click="OnFileMenu"
                                    ToolTip="{DynamicResource L.File}" Padding="9,6">
                                <StackPanel Orientation="Horizontal">
                                    <Viewbox Width="16" Height="16" Margin="0,0,7,0">
                                        <Canvas Width="24" Height="24">
                                            <Path Data="{StaticResource Icon.File}"
                                                  Stroke="{DynamicResource Text}"
                                                  StrokeThickness="1.8" />
                                        </Canvas>
                                    </Viewbox>
                                    <TextBlock Text="{DynamicResource L.File}"
                                               VerticalAlignment="Center" />
                                </StackPanel>
                            </Button>

                            <Button Style="{StaticResource IconButton}"
                                    Content="{StaticResource Icon.Open}"
                                    ToolTip="{DynamicResource L.Open}"
                                    Click="OnOpen" Margin="2,0,0,0" />
                        </StackPanel>

                        <!-- 中间：文件名 + 路径，两者都由代码后置更新 -->
                        <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center"
                                    MaxWidth="330">
                            <TextBlock x:Name="HeaderTitle" Text="MokiPad"
                                       FontSize="16" FontWeight="SemiBold"
                                       TextAlignment="Center"
                                       TextTrimming="CharacterEllipsis" />
                            <TextBlock x:Name="HeaderPath" FontSize="11"
                                       Foreground="{DynamicResource Secondary}"
                                       TextTrimming="CharacterEllipsis"
                                       Visibility="Collapsed" />
                        </StackPanel>

                        <!-- 右侧：置顶、最小化、最大化、关闭 -->
                        <StackPanel HorizontalAlignment="Right" Orientation="Horizontal"
                                    VerticalAlignment="Center"
                                    shell:WindowChrome.IsHitTestVisibleInChrome="True">
                            <Button x:Name="PinButton" Style="{StaticResource IconButton}"
                                    Content="{StaticResource Icon.Pin}"
                                    ToolTip="{DynamicResource L.Pin}" Click="OnPin" />
                            <Button Style="{StaticResource IconButton}"
                                    Content="{StaticResource Icon.Minimize}"
                                    ToolTip="{DynamicResource L.Minimize}" Click="OnMinimize" />
                            <Button Style="{StaticResource IconButton}"
                                    Content="{StaticResource Icon.Maximize}"
                                    ToolTip="{DynamicResource L.Maximize}" Click="OnMaximize" />
                            <Button Style="{StaticResource IconButton}"
                                    Content="{StaticResource Icon.Close}"
                                    ToolTip="{DynamicResource L.CloseWindow}" Click="OnExit" />
                        </StackPanel>
                    </Grid>
                </Border>

                <!-- 第 1 行：标签栏 -->
                <Border Grid.Row="1" BorderBrush="{DynamicResource Border}"
                        BorderThickness="0,0,0,1">
                    <DockPanel x:Name="TabStrip" LastChildFill="False"
                               Margin="8,0" SizeChanged="OnTabStripSizeChanged">

                        <!-- 标签列表：横向排列，绑定 DocumentTab -->
                        <ListBox x:Name="Tabs" DockPanel.Dock="Left"
                                 SelectionChanged="OnTabSelected"
                                 ItemContainerStyle="{StaticResource DocumentTabItem}"
                                 MouseUp="OnTabMouseUp"
                                 PreviewMouseWheel="OnTabWheel"
                                 ContextMenuOpening="OnTabContextMenu">
                            <ListBox.ItemsPanel>
                                <ItemsPanelTemplate>
                                    <StackPanel Orientation="Horizontal" />
                                </ItemsPanelTemplate>
                            </ListBox.ItemsPanel>

                            <!-- 每个标签长什么样：文件名 + 关闭按钮 -->
                            <ListBox.ItemTemplate>
                                <DataTemplate>
                                    <StackPanel Orientation="Horizontal">
                                        <TextBlock Text="{Binding Name}" FontSize="14"
                                                   MaxWidth="220"
                                                   TextTrimming="CharacterEllipsis"
                                                   ToolTip="{Binding FilePath}" />
                                        <Button Style="{StaticResource IconButton}"
                                                Content="{StaticResource Icon.Close}"
                                                ToolTip="{DynamicResource L.Close}"
                                                Click="OnCloseSpecificTab" />
                                    </StackPanel>
                                </DataTemplate>
                            </ListBox.ItemTemplate>
                        </ListBox>

                        <!-- 标签栏末尾的“新建/打开”按钮，悬停时才显示 -->
                        <Button DockPanel.Dock="Left"
                                Content="{StaticResource Icon.New}"
                                ToolTip="{DynamicResource L.Open}"
                                Click="OnOpen" Margin="2,3">
                            <Button.Style>
                                <Style TargetType="Button"
                                       BasedOn="{StaticResource IconButton}">
                                    <Setter Property="Opacity" Value="0" />
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding IsMouseOver, ElementName=TabStrip}"
                                                     Value="True">
                                            <Setter Property="Opacity" Value="1" />
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </Button.Style>
                        </Button>
                    </DockPanel>
                </Border>

                <!-- 第 2 行：阅读区。PreviewHost 是空边框，代码把阅读器放进去 -->
                <Grid Grid.Row="2" PreviewMouseWheel="OnZoomWheel">
                    <Border x:Name="PreviewHost" Padding="0,12,0,12" />

                    <!-- 欢迎页：打开文件后隐藏 -->
                    <StackPanel x:Name="Welcome" HorizontalAlignment="Center"
                                VerticalAlignment="Center" MaxWidth="430" Margin="28">
                        <Viewbox Width="42" Height="42" HorizontalAlignment="Center">
                            <Canvas Width="24" Height="24">
                                <Path Data="{StaticResource Icon.Notebook}"
                                      Stroke="{DynamicResource Accent}"
                                      StrokeThickness="1.4" />
                            </Canvas>
                        </Viewbox>
                        <TextBlock Text="{DynamicResource L.ReaderWelcome}"
                                   FontSize="24" FontWeight="SemiBold"
                                   TextAlignment="Center" />
                        <TextBlock Text="{DynamicResource L.ReaderHint}"
                                   Foreground="{DynamicResource Secondary}"
                                   FontSize="13" TextWrapping="Wrap"
                                   TextAlignment="Center" Margin="0,12,0,22" />
                        <Button Content="{DynamicResource L.OpenMarkdown}"
                                Click="OnOpen"
                                Foreground="{DynamicResource Accent}"
                                Background="{DynamicResource MdSoft}"
                                Padding="20,10" HorizontalAlignment="Center" />
                    </StackPanel>
                </Grid>

                <!-- 第 3 行：底部状态栏 -->
                <Border Grid.Row="3" BorderBrush="{DynamicResource Border}"
                        BorderThickness="0,1,0,0">
                    <Grid Margin="10,0">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="Auto" />
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>

                        <!-- 左：主题、设置 -->
                        <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                            <Button x:Name="ThemeButton" Style="{StaticResource IconButton}"
                                    Content="{StaticResource Icon.Theme}"
                                    ToolTip="{DynamicResource L.Theme}" Click="OnTheme" />
                            <Button Style="{StaticResource IconButton}"
                                    Content="{StaticResource Icon.Gear}"
                                    ToolTip="{DynamicResource L.Settings}" Click="OnSettings" />
                        </StackPanel>

                        <!-- 中：状态、路径；右：字数/编码 -->
                        <TextBlock x:Name="Status" Grid.Column="1"
                                   Foreground="{DynamicResource Secondary}"
                                   FontSize="11" Margin="14,0" />
                        <TextBlock x:Name="PathText" Grid.Column="2"
                                   Foreground="{DynamicResource Secondary}"
                                   FontSize="11" TextTrimming="CharacterEllipsis" />
                        <TextBlock x:Name="DocumentInfo" Grid.Column="3"
                                   Foreground="{DynamicResource Secondary}"
                                   FontSize="11" />
                    </Grid>
                </Border>
            </Grid>
        </Grid>
    </Border>
</Window>
```

### 2.5.3 Styles/Controls.xaml：控件模板怎么画

```xml
<!-- 一个带键的按钮样式：只影响引用它的按钮 -->
<Style x:Key="IconButton" TargetType="Button">
    <Setter Property="Width" Value="32" />
    <Setter Property="Height" Value="32" />
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="Template">
        <Setter.Value>
            <!-- ControlTemplate 决定按钮的“骨架” -->
            <ControlTemplate TargetType="Button">
                <Border x:Name="Chrome"
                        Background="{TemplateBinding Background}"
                        CornerRadius="6">
                    <ContentPresenter HorizontalAlignment="Center"
                                      VerticalAlignment="Center" />
                </Border>
                <!-- Trigger：鼠标悬停/按下时换背景 -->
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter TargetName="Chrome" Property="Background"
                                Value="{DynamicResource Hover}" />
                    </Trigger>
                    <Trigger Property="IsPressed" Value="True">
                        <Setter TargetName="Chrome" Property="Background"
                                Value="{DynamicResource Selected}" />
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
```

### 2.5.4 XAML 和 C# 是怎么对上的

| XAML | C# | 对应方式 |
| --- | --- | --- |
| `x:Class="MokiPad.MainWindow"` | `public partial class MainWindow` | 编译时把 XAML 生成到同一个类 |
| `x:Name="Tabs"` | `Tabs` 字段 | `InitializeComponent()` 生成字段并赋值 |
| `Click="OnOpen"` | `private void OnOpen(object sender, RoutedEventArgs e)` | 事件签名匹配后自动挂接 |
| `SelectionChanged="OnTabSelected"` | `OnTabSelected(object, SelectionChangedEventArgs)` | 事件处理器 |
| `Tabs.ItemsSource = documents` | `ObservableCollection<DocumentTab> documents` | 代码后置设置数据源 |
| `{Binding Name}` / `{Binding FilePath}` | `DocumentTab.Name` / `DocumentTab.FilePath` | 数据模板的当前项是 `DocumentTab` |
| `PreviewHost.Child = preview` | `Border PreviewHost` | 代码把阅读器塞进 XAML 预留的空边框 |
| `x:Name="HeaderTitle"` | `HeaderTitle.Text = tab.Name` | 代码直接改控件属性 |
| `{DynamicResource Surface}` | `ThemeService.Apply` 里的 `Resources["Surface"]` | 资源键对应画刷 |
| `{DynamicResource L.File}` | `UiText.Get("File")` | 资源键对应文案 |

### 2.5.5 这个项目算 MVVM 吗

不算严格 MVVM，它是 **XAML View + 代码后置 + 数据模型**：

- **View（V）**：`MainWindow.xaml`、`Styles/Controls.xaml`、数据模板；
- **Model（M）**：`DocumentTab`、`PreviewBlock`、`MarkdownResult`、`LoadedFile`；
- **ViewModel（VM）**：没有单独的 VM 层；窗口代码后置直接持有 `documents`、`active`、`preferences`、`preview` 并操作控件。

为什么不强行 MVVM：

- 阅读器需要大量命令式操作：滚动位置、虚拟化容器、鼠标选择、剪贴板；
- 单窗口小应用，VM 会带来大量样板；
- 列表数据仍然通过绑定显示（`ItemsSource`、`DataTemplate`），不是完全手写 UI。

严格 MVVM 会是什么样：

```text
MainWindow.xaml（V）
  → 绑定 MainWindowViewModel（VM）
    → ObservableCollection<DocumentTab>（M）
    → ICommand OpenCommand / CloseCommand
    → SelectedTab、StatusText
```

当前项目的实际结构：

```text
MainWindow.xaml（V）
  → MainWindow.xaml.cs（视图逻辑，直接操作控件）
    → Preferences / ThemeService / UiText（服务）
    → DocumentTab / PreviewBlock（模型）
```

所以：
- **V 是 XAML**，不是“没有 V”；
- 代码后置不是 VM，它是 View 的代码部分；
- 如果以后要 MVVM，先抽 `MainWindowViewModel`，把 `documents`、`active`、命令、状态文本放进去，XAML 改成绑定 VM。

---

## 3. 启动与窗口

**文件**：`src/MokiPad/App.xaml.cs`

```csharp
public partial class App // App 由 App.xaml 生成另一半，所以是 partial
{
    protected override void OnStartup(StartupEventArgs e) // WPF 启动钩子，e.Args 是命令行参数
    {
        base.OnStartup(e); // 先执行基类逻辑

        var window = new MainWindow(); // 创建主窗口对象
        MainWindow = window; // 赋给应用的 MainWindow 属性
        window.Show(); // 显示窗口，触发第一次布局

        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, // 把打开文件排到空闲时
            new Action(() => _ = OpenFilesAsync(window, e.Args))); // 丢弃 Task，异常在方法内处理
    }

    private static async Task OpenFilesAsync(MainWindow window, string[] arguments) // 异步打开命令行文件
    {
        foreach (var argument in arguments.Where(File.Exists)) // 只处理真实存在的文件
            await window.OpenFileAsync(argument); // 逐个打开，避免并发抢界面
    }
}
```

**文件**：`src/MokiPad/MainWindow.xaml.cs`

```csharp
public MainWindow(Preferences? settings = null) // 可以传入测试设置，正常启动传 null
{
    preferences = settings ?? Preferences.Load(); // 没传就读本地设置
    ThemeService.Apply(preferences.Dark); // 先应用主题，XAML 里的 DynamicResource 才能取色
    UiText.Apply(preferences.Language); // 先设置语言，界面文案才正确

    InitializeComponent(); // 加载 XAML，生成 Tabs、HeaderTitle 等字段

    Tabs.ItemsSource = documents; // 标签列表绑定到可观察集合
    BindKeys(); // 注册快捷键
    initialized = true; // 标记初始化完成，避免首次选中触发渲染
    UpdateThemeButton(false); // 设置太阳/月亮图标
    ShowWelcome(); // 显示欢迎页

    _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ApplyBackground)); // 空闲时加载背景
}
```

---

## 4. 文件读取与编码

**文件**：`src/MokiPad.Core/FileService.cs`

```csharp
public static async Task<LoadedFile> ReadAsync(string path, CancellationToken token = default) // 异步读取，返回文本、编码、换行
{
    string text; // 保存文件文本
    Encoding encoding; // 保存实际编码

    await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, // 打开文件，允许其他程序同时读写
        FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan)) // 异步、顺序读取
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 65536, true); // 先按 UTF-8 读，识别 BOM
        try
        {
            text = await reader.ReadToEndAsync(token); // 异步读完整篇
            encoding = reader.CurrentEncoding; // 记录实际编码
        }
        catch (DecoderFallbackException) // UTF-8 解不出来，可能是 GB18030
        {
            stream.Position = 0; // 回到文件开头
            encoding = Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, // 用严格 GB18030
                DecoderFallback.ExceptionFallback); // 解码失败就抛异常，不静默替换
            using var fallback = new StreamReader(stream, encoding, false, 65536, true); // 重新读
            text = await fallback.ReadToEndAsync(token); // 读取 GB18030 内容
        }
    }

    var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "CRLF" : text.Contains('\r') ? "CR" : "LF"; // 判断换行风格
    return new LoadedFile(text, encoding, nl); // 返回不可变快照
}
```

---

## 5. Markdown 解析

**文件**：`src/MokiPad.Core/MarkdownEngine.cs`

```csharp
private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder() // 只构建一次，重复使用
    .UsePipeTables() // 支持 | 表格
    .UseTaskLists() // 支持 - [x] 任务
    .UseAutoLinks() // 自动识别网址
    .UseEmphasisExtras() // 支持 ~~ == ++ ^^
    .UseMathematics() // 支持 $...$ 和 $$...$$
    .UseFootnotes() // 支持 [^1]
    .Build(); // 生成解析管线

public static MarkdownResult Parse(string source, CancellationToken cancellation = default) // 入口
{
    var watch = Stopwatch.StartNew(); // 计时
    var doc = Markdown.Parse(source, Pipeline); // 把文本解析成语法树
    var blocks = new List<PreviewBlock>(); // 输出块列表
    var anchors = new Dictionary<string, int>(StringComparer.Ordinal); // 标题锚点计数

    foreach (var block in doc) // 遍历顶层节点
        Add(block); // 递归转成 PreviewBlock

    return new MarkdownResult(blocks, watch.Elapsed.TotalMilliseconds); // 返回块和耗时
}
```

```csharp
void Add(Block block, int depth = 0, string prefix = "") // 本地函数，递归处理
{
    cancellation.ThrowIfCancellationRequested(); // 切走后尽快停止
    switch (block) // 按语法树节点类型分派
    {
        case HeadingBlock h: // 标题
            var title = Plain(h.Inline); // 行内内容转纯文本
            blocks.Add(new PreviewBlock("heading", h.Line + 1, h.Span.Start, title, h.Inline, // 记录类型、行号、偏移、标题、Inline
                h.Level, Anchor: UniqueAnchor(title))); // 级别和唯一锚点
            break; // 结束标题分支
        case MathBlock math: // 块级公式
            var tex = math.Lines.ToString().Trim(); // 取出 TeX
            if (tex.Length > 0) // 非空才添加
                blocks.Add(new PreviewBlock("math", math.Line + 1, math.Span.Start, tex)); // 公式块
            break; // 结束公式分支
        case FencedCodeBlock code: // 代码围栏
            AddChunks(code.Lines.ToString(), "code", code.Line + 1, code.Span.Start, code.Info ?? ""); // 大代码块切分
            break; // 结束代码分支
        case Table table: // 表格
            // 把表格转成行和单元格，见完整文件
            break; // 结束表格分支
        case ParagraphBlock paragraph: // 普通段落
            // 把段落转成 paragraph 块
            break; // 结束段落分支
    }
}
```

---

## 6. 阅读列表与滚动

**文件**：`src/MokiPad/Controls/MarkdownPreview.cs`

```csharp
public MarkdownPreview() // 构造函数
{
    SetValue(ScrollViewer.CanContentScrollProperty, true); // 由列表面板管理滚动
    SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled); // 不要横向滚动条
    SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto); // 需要时显示纵向滚动条

    SetValue(VirtualizingPanel.IsVirtualizingProperty, true); // 开启虚拟化
    SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling); // 回收容器
    SetValue(VirtualizingPanel.ScrollUnitProperty, ScrollUnit.Pixel); // 按像素滚动
    SetValue(VirtualizingPanel.CacheLengthProperty, new VirtualizationCacheLength(1)); // 前后缓存一页

    ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel))); // 纵向虚拟化面板
    BorderThickness = new Thickness(0); // 去掉边框
    Background = Brushes.Transparent; // 透明背景
    HorizontalContentAlignment = HorizontalAlignment.Stretch; // 内容撑满宽度

    AddHandler(PreviewMouseWheelEvent, new MouseWheelEventHandler(OnReadingWheel), true); // 隧道接住滚轮
    Unloaded += (_, _) => StopScrolling(); // 卸载时停止动画
}
```

```csharp
private void OnReadingWheel(object sender, MouseWheelEventArgs e) // 滚轮入口
{
    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 || e.Delta == 0) return; // Ctrl 用于缩放，零增量无效

    readingScroll ??= DescendantScroll(this); // 第一次滚动时找内部滚动条
    if (readingScroll is null) return; // 找不到就不处理

    e.Handled = true; // 阻止内部代码块再滚一次
    var direction = Math.Sign(e.Delta); // 正数向上，负数向下

    if (!scrolling || direction != wheelDirection) // 新滚动或方向反转
    {
        remainingScroll = 0; // 清空旧方向剩余距离
        scrollRemainder = 0; // 清空像素余量
        if (scrolling) readingScroll.ScrollToVerticalOffset(readingScroll.VerticalOffset); // 立即停在当前位置
    }

    wheelDirection = direction; // 记录方向

    var lines = SystemParameters.WheelScrollLines; // 系统“一次滚几行”
    var distance = lines < 0 // 小于 0 表示按页
        ? readingScroll.ViewportHeight * .9 // 一页的 90%
        : Math.Clamp(lines, 1, 8) * (double)GetValue(ReadingFontSizeProperty) * 1.75 * 1.25; // 行数转像素

    remainingScroll = Math.Clamp(remainingScroll - e.Delta / 120.0 * distance, // 累加剩余距离
        -readingScroll.VerticalOffset, // 不能小于顶部
        Math.Max(0, readingScroll.ScrollableHeight - readingScroll.VerticalOffset)); // 不能超过底部

    if (!SystemParameters.ClientAreaAnimation) // 系统关闭动画
    {
        readingScroll.ScrollToVerticalOffset(ScrollTargetOffset); // 直接跳
        remainingScroll = 0; // 清空剩余
        return; // 结束
    }

    if (scrolling) return; // 已经在动画中，只更新目标

    scrolling = true; // 开始动画
    lastScrollTick = Stopwatch.GetTimestamp(); // 记录时间
    CompositionTarget.Rendering += AdvanceScroll; // 每帧回调
}
```

---

## 7. 块渲染

**文件**：`src/MokiPad/Controls/MarkdownPreview.cs`（`BlockPresenter`）

```csharp
internal void Render() // 根据块类型生成 WPF 内容
{
    if (Block is not { } block) // 没有数据
    {
        Content = null; // 清空
        return; // 结束
    }

    Margin = new Thickness(32 + Math.Min(block.Level, 8) * (block.Kind == "paragraph" ? 15 : 0), // 左边距按级别缩进
        0, 32, block.Checked.HasValue || block.Prefix.Length > 0 ? 4 : 16); // 右 32，底边距按任务/列表调整
    ToolTip = null; // 清掉旧提示

    switch (block.Kind) // 按类型分派
    {
        case "heading": // 标题
            Margin = new Thickness(32, block.Line == 1 ? 0 : 20, 32, 12); // 第一行标题不加顶边距
            var heading = Text("", block.Level switch { 1 => 33.6, 2 => 24.8, 3 => 20.8, 4 => 17.92, _ => 16 }); // 按级别设字号
            if (block.Inline is not null) Inline(block.Inline, heading.Inlines, heading.FontSize); // 行内节点转 Inline
            else heading.Text = block.Text; // 没有 Inline 就直接显示文本
            heading.FontWeight = FontWeights.SemiBold; // 半粗体
            heading.LineHeight = heading.FontSize * 1.5; // 行高
            if (block.Level >= 5) heading.Foreground = Brush("Secondary"); // 五六级用次要颜色
            var row = new DockPanel(); // 标题行
            switch (block.Level) // 一级/二级加图标
            {
                case 1: // 一级
                    row.HorizontalAlignment = HorizontalAlignment.Center; // 居中
                    // 创建笔记本图标并放左边
                    break; // 结束
                case 2: // 二级
                    var hash = Text("#", heading.FontSize); // 红色 #
                    // 设置颜色、边距并放左边
                    break; // 结束
            }
            row.Children.Add(heading); // 文字放右边
            Content = row; // 作为块内容
            break; // 结束标题

        case "code": // 代码块
            var panel = new DockPanel(); // 上工具栏、下代码
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) }; // 工具栏
            var copy = new Button // 复制按钮
            {
                Style = WpfApp.Current.FindResource("IconButton") as Style, // 图标按钮样式
                Content = WpfApp.Current.FindResource("Icon.Copy"), // 复制图标
                ToolTip = UiText.Get("CopyCode") // 提示文字
            };
            copy.Click += (_, _) => Clipboard.SetText(block.CodeSource ?? block.Text); // 点击复制完整源码
            DockPanel.SetDock(copy, Dock.Right); // 按钮靠右
            bar.Children.Add(copy); // 加入工具栏
            var label = Text(block.Language, 12); // 语言名
            label.Foreground = Brush("Secondary"); // 次要颜色
            bar.Children.Add(label); // 加入工具栏
            var code = Text("", 14); // 代码文本
            code.FontFamily = AppFonts.Code; // 等宽字体
            code.TextWrapping = TextWrapping.NoWrap; // 不自动换行
            ColorCode(block.Text.TrimEnd('\r', '\n'), block.Language, code.Inlines); // 词法着色
            panel.Children.Add(bar); // 工具栏在上
            panel.Children.Add(new ScrollViewer // 横向滚动
            {
                Content = code, // 代码内容
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto // 太长时横向滚动
            });
            Content = new Border { Child = panel, Background = Brush("MdCode") }; // 圆角背景
            break; // 结束代码
    }
}
```

---

## 8. HTML 行内

**文件**：`src/MokiPad/Controls/MarkdownPreview.cs`（`HtmlContext`）

```csharp
public void AddText(string text) // 追加普通文本
{
    if (skip || text.Length == 0) return; // script/style 内或空文本直接跳过
    var value = WebUtility.HtmlDecode(text); // &amp; 等实体还原
    if (value.Length == 0) return; // 还原后为空也跳过

    var run = new Run(value); // WPF 文本片段
    if (style.Bold) run.FontWeight = FontWeights.Bold; // 加粗
    if (style.Italic) run.FontStyle = FontStyles.Italic; // 斜体
    if (style.Code || style.Kbd) // 代码/键盘样式
    {
        run.FontFamily = AppFonts.Code; // 等宽字体
        run.Background = WpfApp.Current.FindResource("MdSoft") as Brush; // 浅色背景
        run.FontSize = FontSize * .88; // 稍微缩小
    }

    if (style.Mark) run.Background = WpfApp.Current.FindResource("MdSoft") as Brush; // 高亮
    if (style.Sup) // 上标
    {
        run.BaselineAlignment = BaselineAlignment.Superscript; // 对齐方式
        run.FontSize *= .72; // 缩小
    }
    if (style.Sub) // 下标
    {
        run.BaselineAlignment = BaselineAlignment.Subscript; // 对齐方式
        run.FontSize *= .72; // 缩小
    }
    if (style.Small) run.FontSize *= .86; // 小号
    if (style.Underline || style.Strike) // 下划线/删除线
    {
        var decorations = new TextDecorationCollection(); // 装饰集合
        if (style.Underline) decorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline }); // 下划线
        if (style.Strike) decorations.Add(new TextDecoration { Location = TextDecorationLocation.Strikethrough }); // 删除线
        run.TextDecorations = decorations; // 应用
    }

    Target.Add(run); // 加入当前 Inline 集合
}
```

---

## 9. 图片与 SVG

**文件**：`src/MokiPad/Controls/ImageLoader.cs`

```csharp
public static Task<ImageSource?> LoadAsync(string source, string? documentPath, int pixelWidth = 0) // 加载图片入口
{
    var key = Key(source, documentPath); // 生成缓存键
    var width = pixelWidth <= 0 ? 0 : (int)Math.Ceiling(pixelWidth / 256.0) * 256; // 按 256 像素分档

    return key is null // 地址无效
        ? Task.FromResult<ImageSource?>(null) // 返回空
        : Cache.GetAsync(key + ":" + width, // 查缓存
            () => Task.Run(() => LoadCoreAsync(source, documentPath, width)), // 未命中则后台加载
            image => image is BitmapSource bitmap // 计算内存权重
                ? (long)bitmap.PixelWidth * bitmap.PixelHeight * 4 // 位图按像素
                : 64 * 1024); // 矢量图按固定值
}
```

```csharp
private static BitmapImage? DecodeBitmap(byte[] bytes, int requestedWidth) // 解码位图
{
    int width; // 原始像素宽度
    using (var probe = new MemoryStream(bytes)) // 临时流
    {
        var decoder = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None); // 轻量探测
        width = decoder.Frames[0].PixelWidth; // 读原始宽度
    }

    using var stream = new MemoryStream(bytes); // 重新打开流
    var image = new BitmapImage(); // WPF 位图
    image.BeginInit(); // 开始批量设置
    image.CacheOption = BitmapCacheOption.OnLoad; // 解码后立即读入内存
    image.DecodePixelWidth = requestedWidth > 0 && width > requestedWidth ? requestedWidth : 0; // 按显示宽度缩小
    image.StreamSource = stream; // 指定位图数据
    image.EndInit(); // 结束设置并真正解码
    image.Freeze(); // 冻结成只读，可跨线程
    return image; // 返回
}
```

---

## 10. GIF 播放

**文件**：`src/MokiPad/Controls/GifAnimation.cs`

```csharp
private async Task DecodeAsync() // 后台解码循环
{
    var token = cancellation.Token; // 取消令牌
    using var data = SKData.CreateCopy(bytes); // 把字节交给 Skia
    using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Invalid GIF"); // 创建 GIF 解码器

    FrameCount = Math.Max(1, codec.FrameCount); // 总帧数
    var width = codec.Info.Width; // 原始宽
    var height = codec.Info.Height; // 原始高

    var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul); // BGRA 像素格式
    using var canvas = new SKBitmap(info); // 一帧像素缓冲
    canvas.Erase(SKColors.Transparent); // 先清空

    await target.Dispatcher.InvokeAsync(() => // 回到界面线程创建 WPF 位图
    {
        surface = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null); // 可写位图
        target.Source = surface; // 只设置一次 Source
        RenderOptions.SetBitmapScalingMode(target, BitmapScalingMode.HighQuality); // 高质量缩放
    }, DispatcherPriority.Background, token); // 低优先级，避免抢界面

    while (!token.IsCancellationRequested) // 一帧一帧解
    {
        var result = codec.GetPixels(info, canvas.GetPixels(), canvas.RowBytes, // 解出当前帧
            new SKCodecOptions(index, prior)); // 传入帧索引和合成基底
        if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput) // 解失败
            throw new InvalidDataException("GIF decoding failed: " + result); // 抛异常，外层处理

        await target.Dispatcher.InvokeAsync(() => // 回到界面线程写像素
        {
            surface!.WritePixels(new Int32Rect(0, 0, width, height), // 写入同一张位图
                canvas.GetPixels(), canvas.ByteCount, canvas.RowBytes); // 像素数据
            CurrentFrame = index; // 记录当前帧
            ready.TrySetResult(true); // 首帧完成
        }, DispatcherPriority.Background, token); // 低优先级

        await Task.Delay(delay, token); // 按帧间隔等待
    }
}
```

---

## 11. 数学公式

**文件**：`src/MokiPad/Rendering/MathView.cs`

```csharp
private void Rebuild() // 重新排版公式
{
    if (string.IsNullOrWhiteSpace(source)) // 空源码
    {
        Content = null; // 清空
        HasError = false; // 没有错误
        return; // 结束
    }

    try
    {
        WpfTeXFormulaParser.Instance.Parse(source); // 先自己解析，提前捕获非法 TeX
        formula.Foreground = mathForeground; // 颜色
        formula.Scale = Scale; // 缩放
        formula.Formula = source; // 交给 WpfMath 排版
        HasError = formula.HasError; // 库报告的错误
        Content = HasError ? Fallback() : formula; // 成功显示公式，失败显示原文
    }
    catch (Exception ex) when (ex is TexException or ArgumentException or InvalidOperationException) // 捕获 TeX 异常
    {
        HasError = true; // 标记错误
        Content = Fallback(); // 回退文本
    }
}
```

---

## 12. 选择与复制

**文件**：`src/MokiPad.Core/MarkdownCopy.cs`

```csharp
public static string Selection(string source, IReadOnlyList<PreviewBlock> blocks, // 源文本、块列表
    ReadingPosition anchor, ReadingPosition focus, bool all = false) // 起点、终点、是否全选
{
    if (all) return source; // Ctrl+A 直接返回全文
    var (start, end) = Compare(anchor, focus) <= 0 ? (anchor, focus) : (focus, anchor); // 保证起点在前
    if (start == end || blocks.Count == 0) return ""; // 空选区

    var pieces = new List<(PreviewBlock Block, SourceRange Range)>(); // 收集片段
    for (var index = Math.Max(0, start.Block); index <= Math.Min(end.Block, blocks.Count - 1); index++) // 遍历覆盖的块
    {
        var block = blocks[index]; // 当前块
        var range = Range(block); // 块的源码范围
        if (!range.Valid || range.End > source.Length) continue; // 无效范围跳过

        if (!IsAtomic(block)) // 非原子块才按选区裁剪
            range = new(Math.Max(range.Start, index == start.Block ? start.Offset : range.Start), // 起点
                Math.Min(range.End, index == end.Block ? end.Offset : range.End)); // 终点

        if (!range.Valid) continue; // 裁剪后无效
        if (pieces.Count > 0 && pieces[^1].Range == range) continue; // 去重
        pieces.Add((block, range)); // 加入片段
    }

    var result = new StringBuilder(); // 最终 Markdown
    // 拼接片段，并补齐引用式链接/脚注定义
    return result.ToString(); // 返回
}
```

---

## 13. 主题与字体

**文件**：`src/MokiPad/Services/ThemeService.cs`

```csharp
public static void Apply(bool dark, bool animate = false) // 应用主题
{
    var values = new Dictionary<string, string> // 颜色名到颜色值
    {
        ["Surface"] = dark ? "#1E1E1E" : "#FFFFFF", // 面板背景
        ["Text"] = dark ? "#F5F5F7" : "#2C2C2E", // 正文
        ["Accent"] = dark ? "#5A9FFF" : "#4091FF" // 强调色
    };

    foreach (var (name, value) in values) // 逐个替换
    {
        var color = (Color)ColorConverter.ConvertFromString(value); // 字符串转颜色
        var previous = Application.Current.Resources[name] as SolidColorBrush; // 旧画刷
        var brush = new SolidColorBrush(animate && previous is not null ? previous.Color : color); // 新画刷

        if (animate && previous is not null && SystemParameters.ClientAreaAnimation) // 需要动画
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty, // 颜色动画
                new ColorAnimation(color, TimeSpan.FromMilliseconds(220))); // 220 毫秒渐变
        }
        else
        {
            brush.Color = color; // 直接设置
            brush.Freeze(); // 冻结，性能更好
        }

        Application.Current.Resources[name] = brush; // 替换资源，DynamicResource 自动更新
    }
}
```

---

## 14. 设置与本地化

**文件**：`src/MokiPad/Services/Preferences.cs`

```csharp
public sealed class Preferences // 本地设置
{
    public bool Dark { get; set; } // 深色主题
    public string Language { get; set; } = "zh"; // 界面语言，默认中文
    public double FontSize { get; set; } = 16; // 阅读字号

    public static Preferences Load() // 读取设置
    {
        try
        {
            return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsFile)) // 读 JSON
                ?? new Preferences(); // 空文件用默认
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) // 读失败
        {
            return new Preferences(); // 回退默认，不让阅读器打不开
        }
    }
}
```

---

## 15. 菜单与快捷键

**文件**：`src/MokiPad/MainWindow.xaml.cs`

```csharp
private void BindKeys() // 注册快捷键
{
    Bind(Key.O, ModifierKeys.Control, () => OnOpen(this, new RoutedEventArgs())); // Ctrl+O 打开
    Bind(Key.W, ModifierKeys.Control, () => CloseTab(active)); // Ctrl+W 关闭
    Bind(Key.F5, ModifierKeys.None, () => _ = ReloadAsync()); // F5 重新加载
    Bind(Key.F11, ModifierKeys.None, () => OnMaximize(this, new RoutedEventArgs())); // F11 最大化

    return; // 分隔主流程和本地函数

    void Bind(Key key, ModifierKeys mods, Action action) // 本地函数
    {
        var command = new RoutedCommand(); // 命令对象
        CommandBindings.Add(new CommandBinding(command, (_, _) => action())); // 命令执行动作
        InputBindings.Add(new KeyBinding(command, key, mods)); // 按键映射到命令
    }
}
```

---

## 16. 验证

**文件**：`tests/MokiPad.Verification/Program.cs`

```csharp
[STAThread] // WPF 需要 STA 线程
public static int Main(string[] args) // 验证入口
{
    output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts"); // 输出目录
    scrollOnly = args.Contains("--scroll-only"); // 只跑滚动检查
    Directory.CreateDirectory(output); // 确保目录存在

    var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; // 创建应用但不显示窗口
    app.InitializeComponent(); // 加载资源
    var result = 1; // 默认失败

    _ = app.Dispatcher.InvokeAsync(async () => // 在界面线程跑
    {
        try
        {
            await Run(); // 执行全部检查
            result = 0; // 成功
        }
        catch (Exception ex) // 失败
        {
            Console.Error.WriteLine(ex); // 打印异常
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, "verification.json"), // 写报告
                JsonSerializer.Serialize(Metrics, JsonOptions)); // 序列化指标
            app.Shutdown(result); // 关闭应用
        }
    });

    Dispatcher.Run(); // 运行界面消息循环
    return result; // 返回退出码
}
```

---

## 17. 知识树对应

配套 `docs/code-annotated.mgtree`：

- 根节点：本文标题；
- 二级节点：每个模块；
- 代码节点：`>csharp[n]` 跳到对应代码块；
- 序号按语言分别计数。

```tree
MokiPad 逐行注释版代码导读
  3. 启动与窗口
    App 入口 >csharp[1]
    主窗口构造 >csharp[2]
  6. 阅读列表与滚动
    滚轮入口 >csharp[5]
```

用 MokiDown 打开即可按节点跳转。
