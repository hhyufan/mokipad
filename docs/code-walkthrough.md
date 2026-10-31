# MokiPad 代码导读：文件架构、功能模块与逐行实现

> 这份文档先给你完整文件树，再给功能模块清单，最后按模块逐行讲代码。  
> 适合“知道 C# 大概是什么，但不知道这个项目从哪看起”的读者。  
> 语言基础另见 `docs/beginner-guide.md`；本文只讲这个项目本身。  
> 想看 XAML（View）怎么画、怎么和 C# 对应、MVVM 怎么套，见 `docs/code-annotated.md` 的 2.5 节。

## 0. 阅读约定

- 每个模块先列“涉及哪些文件”，再列“调用链”，最后逐行解释代码。
- 源码文件本身已经写了中文行内注释；本文不重复贴出全部 5000 多行，而是按模块拆开入口和关键路径。
- 每个模块末尾有“本模块其余方法”表，列出同一文件里没有展开的方法，方便你回到源码继续看。
- 代码块里的 `...` 表示省略的同类分支，不是让你输入。
- 行号是代码块内的行号，不是源文件行号。

---

## 1. 完整文件架构

```text
mokipad/
├─ MokiPad.sln                         解决方案（Visual Studio / Rider）
├─ MokiPad.slnx                        新版解决方案格式
├─ global.json                        固定 .NET SDK 版本
├─ MokiPad.sln.DotSettings            ReSharper 命名与检查设置
├─ .editorconfig                      编辑器与分析器规则
├─ .gitignore                         忽略构建产物、缓存和本地文件
├─ LICENSE                            MIT 许可证
├─ THIRD-PARTY-NOTICES.md             第三方库与字体许可
├─ README.md                          项目介绍、运行方式、功能清单
├─ scripts/
│  ├─ dev.ps1                         启动开发版阅读器
│  ├─ publish.ps1                     发布到 artifacts/app
│  └─ verify.ps1                      运行离屏验证
├─ docs/
│  ├─ beginner-guide.md                C# / WPF 语言基础
│  ├─ beginner-guide.mgtree           语言基础的知识树
│  ├─ code-walkthrough.md             本文
│  ├─ code-walkthrough.mgtree         本文的知识树
│  ├─ solution.html                   解决方案与检查报告说明
│  ├─ code-quality-report.html        逐条代码质量报告
│  ├─ code-cleanup-changes.md         按修改类型整理的清理记录
│  ├─ markdown-copy.md                选择复制行为说明
│  ├─ scrolling-fix.md                滚动回弹修复记录
│  └─ images/                         README 与文档截图
├─ src/
│  ├─ MokiPad.Core/                   不依赖 WPF 的核心库
│  │  ├─ MokiPad.Core.csproj
│  │  ├─ MarkdownEngine.cs            Markdown → PreviewBlock
│  │  ├─ FileService.cs               文件读取与编码识别
│  │  ├─ CodeSyntax.cs                代码块词法着色
│  │  └─ MarkdownCopy.cs              选区 → 原始 Markdown
│  └─ MokiPad/                        WPF 界面
│     ├─ MokiPad.csproj
│     ├─ App.xaml                     全局资源入口
│     ├─ App.xaml.cs                  应用启动
│     ├─ GlobalUsings.cs              全局 using
│     ├─ MainWindow.xaml              主窗口布局
│     ├─ MainWindow.xaml.cs           窗口逻辑
│     ├─ Models/
│     │  └─ DocumentTab.cs            标签页数据
│     ├─ Controls/
│     │  ├─ MarkdownPreview.cs        虚拟化阅读列表
│     │  ├─ MarkdownSelection.cs      鼠标选择与选区绘制（partial）
│     │  ├─ HtmlBlocks.cs             块级 HTML 切分
│     │  ├─ ImageLoader.cs            图片、SVG、远程缓存
│     │  ├─ BoundedImageCache.cs      有界异步缓存
│     │  └─ GifAnimation.cs           GIF 逐帧播放
│     ├─ Rendering/
│     │  └─ MathView.cs               TeX 公式宿主
│     ├─ Services/
│     │  ├─ Preferences.cs            本地设置
│     │  ├─ ThemeService.cs           明暗主题
│     │  ├─ AppFonts.cs               打包字体
│     │  ├─ UiText.cs                 中英文文案
│     │  ├─ Motion.cs                 图标切换动画
│     │  └─ MarkdownClipboard.cs      剪贴板写入
│     ├─ Styles/
│     │  └─ Controls.xaml             全局样式与模板
│     ├─ Assets/
│     │  ├─ app.ico                   程序图标
│     │  ├─ moki-icon.png             文档图标
│     │  ├─ starlight.png             默认背景
│     │  ├─ Fonts/                    Poppins、JetBrains Mono、中文字体
│     │  └─ Licenses/                 SkiaSharp 等许可
│     └─ Properties/
│        └─ AssemblyInfo.cs           程序集元数据
└─ tests/
   └─ MokiPad.Verification/
      ├─ MokiPad.Verification.csproj
      ├─ Program.cs                  验证入口与主要检查
      ├─ MarkdownCopyVerification.cs  复制行为检查
      ├─ ScrollReplay.cs             逐帧滚动回放
      └─ NativeScrollReplay.cs       真实 WPF 渲染队列回放
```

---

## 2. 功能模块总览

| 模块 | 入口 | 主要文件 | 结果 |
| --- | --- | --- | --- |
| 应用启动与窗口壳 | `App.OnStartup` | `App.xaml(.cs)`、`MainWindow.xaml(.cs)` | 显示窗口、标签、菜单 |
| 文件读取与编码 | `MainWindow.OpenFileAsync` | `FileService.cs`、`DocumentTab.cs` | `LoadedFile` |
| Markdown 解析 | `MarkdownEngine.Parse` | `MarkdownEngine.cs`、`MarkdownCopy.cs` | `MarkdownResult` |
| 阅读列表与滚动 | `MarkdownPreview` | `MarkdownPreview.cs` | 虚拟化滚动列表 |
| 块渲染 | `BlockPresenter.Render` | `MarkdownPreview.cs` | 标题、段落、代码、表格 |
| HTML 行内 | `HtmlContext.ApplyTag` | `HtmlBlocks.cs`、`MarkdownPreview.cs` | WPF `Inline` |
| 图片与 SVG | `ImageLoader.LoadAsync` | `ImageLoader.cs`、`BoundedImageCache.cs` | `ImageSource` |
| GIF 播放 | `GifAnimation.StartAsync` | `GifAnimation.cs` | 逐帧位图 |
| 数学公式 | `MathView.Rebuild` | `MathView.cs` | `FormulaControl` |
| 选择与复制 | `MarkdownSelection` | `MarkdownSelection.cs`、`MarkdownCopy.cs`、`MarkdownClipboard.cs` | 原始 Markdown |
| 主题、样式、字体 | `ThemeService.Apply` | `ThemeService.cs`、`Controls.xaml`、`AppFonts.cs` | 画刷与样式 |
| 设置与本地化 | `Preferences` | `Preferences.cs`、`UiText.cs` | 本地偏好、界面文案 |
| 菜单、快捷键、背景 | `MainWindow.BindKeys` | `MainWindow.xaml.cs` | 菜单、缩放、背景 |
| 验证 | `Program.Main` | `tests/MokiPad.Verification` | 检查报告 |

---

## 3. 模块逐行讲解

### 3.1 应用启动与窗口壳

**涉及文件**

- `src/MokiPad/App.xaml`
- `src/MokiPad/App.xaml.cs`
- `src/MokiPad/MainWindow.xaml`
- `src/MokiPad/MainWindow.xaml.cs`

**调用链**

```text
WPF 创建 App
  → App.OnStartup
    → new MainWindow()
      → InitializeComponent()
      → BindKeys()
      → ShowWelcome()
    → window.Show()
    → Dispatcher.BeginInvoke(OpenFilesAsync)
```

#### 3.1.1 应用入口

```csharp
public partial class App
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,
            new Action(() => _ = OpenFilesAsync(window, e.Args)));
    }
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 1 | `public partial class App` | `App` 由 `App.xaml` 生成另一半，所以是 `partial` |
| 3 | `OnStartup(StartupEventArgs e)` | WPF 启动钩子；`e.Args` 是命令行参数 |
| 5 | `base.OnStartup(e)` | 先执行基类逻辑，别跳过 |
| 7 | `new MainWindow()` | 创建主窗口对象，但还没显示 |
| 8 | `MainWindow = window` | 把窗口赋给应用的 `MainWindow` 属性，方便统一关闭 |
| 9 | `window.Show()` | 真正显示窗口，触发第一次布局和绘制 |
| 11 | `Dispatcher.BeginInvoke` | 把启动文件的工作排到界面线程队列，稍后执行 |
| 11 | `DispatcherPriority.ContextIdle` | 优先级很低，等窗口先画出来 |
| 12 | `_ = OpenFilesAsync(...)` | 故意丢弃返回的 `Task`；异常由方法内部处理 |

#### 3.1.2 主窗口构造函数

```csharp
public MainWindow(Preferences? settings = null)
{
    preferences = settings ?? Preferences.Load();
    ThemeService.Apply(preferences.Dark);
    UiText.Apply(preferences.Language);

    InitializeComponent();

    Tabs.ItemsSource = documents;
    BindKeys();
    initialized = true;
    UpdateThemeButton(false);
    ShowWelcome();

    _ = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ApplyBackground));
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3 | `settings ?? Preferences.Load()` | 没传设置就读本地设置 |
| 4 | `ThemeService.Apply(...)` | 先应用主题，XAML 里的 `DynamicResource` 才能取到颜色 |
| 5 | `UiText.Apply(...)` | 先设置语言，界面文案才是正确语言 |
| 7 | `InitializeComponent()` | 加载 XAML、创建控件、把 `x:Name` 绑定到字段 |
| 9 | `Tabs.ItemsSource = documents` | 标签列表绑定到可观察集合 |
| 10 | `BindKeys()` | 注册快捷键 |
| 11 | `initialized = true` | 标记初始化完成，避免首次选中标签时误触发渲染 |
| 12 | `UpdateThemeButton(false)` | 根据主题设置太阳/月亮图标 |
| 13 | `ShowWelcome()` | 显示欢迎页 |
| 15 | `Dispatcher.BeginInvoke(...ApplyBackground)` | 背景图解码推迟到空闲时，避免拖慢启动 |

#### 3.1.3 打开文件

```csharp
public async Task OpenFileAsync(string path)
{
    await openGate.WaitAsync();
    try
    {
        path = Path.GetFullPath(path);
        if (documents.FirstOrDefault(d => d.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)) is { } existing)
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
    finally
    {
        openGate.Release();
    }
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3 | `await openGate.WaitAsync()` | 一次只允许一个打开操作，避免连续拖入多个文件时同时读盘 |
| 5 | `Path.GetFullPath(path)` | 转成绝对路径，便于比较重复文件 |
| 6 | `FirstOrDefault(...)` | 如果同一路径已经打开，直接切过去 |
| 8 | `Tabs.SelectedItem = existing` | 选中已有标签 |
| 12 | `await Task.Run(() => FileService.ReadAsync(path))` | 文件读取放到线程池，不阻塞界面 |
| 13 | `new DocumentTab { ... }` | 创建只读标签数据 |
| 14 | `documents.Add(tab)` | 集合变化会自动更新 `ListBox` |
| 15 | `Tabs.SelectedItem = tab` | 选中新标签，触发 `OnTabSelected` |
| 19 | `openGate.Release()` | 无论成功失败都释放锁 |

#### 3.1.4 解析并显示

```csharp
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

    await parseGate.WaitAsync(token);
    try
    {
        var result = await Task.Run(() => tab.IsMarkdown
            ? MarkdownEngine.Parse(tab.File.Text, token)
            : PlainText(tab.File.Text, token), token);

        if (token.IsCancellationRequested || active != tab) return;
        tab.Parsed = result;
        ApplyParsed(tab, result);
    }
    finally
    {
        parseGate.Release();
    }
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3 | `if (active is not { } tab) return` | 没有当前标签就不渲染 |
| 4 | `tab.Parsed is { } cached` | 已经解析过，直接用缓存 |
| 9 | `CancelAsync()` | 取消上一次还没完成的解析 |
| 10-12 | `CancellationTokenSource` | 每次渲染有独立取消令牌 |
| 14 | `parseGate.WaitAsync(token)` | 串行解析，避免两篇长文同时抢处理器 |
| 17-19 | `Task.Run(...)` | Markdown 解析放到线程池 |
| 21 | `token.IsCancellationRequested || active != tab` | 解析完成时可能已经切走，丢弃旧结果 |
| 22 | `tab.Parsed = result` | 缓存解析结果 |
| 23 | `ApplyParsed(...)` | 把块列表交给阅读器 |

#### 3.1.5 把块交给阅读器

```csharp
private void ApplyParsed(DocumentTab tab, MarkdownResult result)
{
    EnsurePreview();
    preview!.DocumentPath = tab.FilePath;
    preview.SetBlocks(result.Blocks);
    Status.Text = UiText.Get("PreviewTime", result.Blocks.Count, result.ParseMs);

    var offset = tab.ScrollOffset;
    _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
    {
        if (active == tab && preview is not null)
            FindChild<ScrollViewer>(preview)?.ScrollToVerticalOffset(offset);
    }));
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3 | `EnsurePreview()` | 第一次需要时创建阅读器并放进 `PreviewHost` |
| 4 | `DocumentPath` | 让图片、链接能解析相对路径 |
| 5 | `SetBlocks(...)` | 替换 `ItemsSource`，列表自动生成可见块 |
| 6 | `Status.Text` | 显示块数和解析耗时 |
| 8 | `tab.ScrollOffset` | 切换回来时恢复上次滚动位置 |
| 9 | `DispatcherPriority.Loaded` | 等新列表完成测量后再恢复，否则滚动高度还是 0 |
| 11 | `ScrollToVerticalOffset(offset)` | 恢复像素位置 |

**本模块其余方法**

| 文件 | 方法 | 作用 |
| --- | --- | --- |
| `MainWindow.xaml.cs` | `ShowWelcome` | 清空阅读区，显示欢迎页 |
| | `OnTabSelected` | 保存旧标签位置、取消旧渲染、渲染新标签 |
| | `EnsurePreview` | 创建阅读器、挂右键菜单、放进 `PreviewHost` |
| | `PlainText` | 纯文本按 4096 字符切块 |
| | `UpdateDocumentInfo` | 底部字数与编码 |
| | `CloseTab` / `OnCloseSpecificTab` | 关闭标签 |
| | `ReloadAsync` / `OnDrop` | 重新加载、拖放打开 |
| | `OnClosing` | 保存设置、取消渲染 |
| | `ApplyBackground` | 加载背景图 |
| | `OnTheme` / `RefreshPreview` | 切换主题并重建阅读器 |
| | `BindKeys` | 快捷键 |
| | `CreateFileMenu` / `CreateSettingsMenu` / `CreateReaderMenu` | 三类菜单 |
| | `FindChild<T>` | 在可视树里找子元素 |

---

### 3.2 文件读取与编码

**涉及文件**

- `src/MokiPad.Core/FileService.cs`
- `src/MokiPad/Models/DocumentTab.cs`

**调用链**

```text
OpenFileAsync
  → FileService.ReadAsync
    → FileStream
    → StreamReader(UTF-8, 检测 BOM)
    → 失败则 GB18030
  → LoadedFile
  → DocumentTab
```

```csharp
public static async Task<LoadedFile> ReadAsync(string path, CancellationToken token = default)
{
    string text;
    Encoding encoding;

    await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 65536, true);
        try
        {
            text = await reader.ReadToEndAsync(token);
            encoding = reader.CurrentEncoding;
        }
        catch (DecoderFallbackException)
        {
            stream.Position = 0;
            encoding = Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback);
            using var fallback = new StreamReader(stream, encoding, false, 65536, true);
            text = await fallback.ReadToEndAsync(token);
        }
    }

    var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "CRLF" : text.Contains('\r') ? "CR" : "LF";
    return new LoadedFile(text, encoding, nl);
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 1 | `async Task<LoadedFile>` | 异步读取，返回文本、编码、换行风格 |
| 6-7 | `FileStream(...)` | 打开文件；`FileShare.ReadWrite` 允许其他程序同时读写 |
| 7 | `FileOptions.Asynchronous` | 使用异步 IO |
| 7 | `SequentialScan` | 告诉系统顺序读取，优化大文件 |
| 9 | `StreamReader(...)` | 用 UTF-8 读取；`detectEncodingFromByteOrderMarks: true` 识别 BOM |
| 12 | `ReadToEndAsync` | 异步读完整篇 |
| 13 | `reader.CurrentEncoding` | 记录实际使用的编码 |
| 15 | `catch (DecoderFallbackException)` | UTF-8 解不出来，说明可能是 GB18030 |
| 17 | `stream.Position = 0` | 回到文件开头重新读 |
| 18-19 | `Encoding.GetEncoding("GB18030", ...)` | 用严格模式，失败就抛异常而不是静默替换 |
| 20 | `new StreamReader(...)` | 用 GB18030 重新读 |
| 25 | `text.Contains("\r\n")` | 判断 CRLF；再判断单个 CR；否则 LF |

`LoadedFile` 只有三个属性：`Text`、`Encoding`、`NewLine`。`DocumentTab` 再补上 `FilePath`、`Parsed`、`ScrollOffset` 等界面状态。

---

### 3.3 Markdown 解析

**涉及文件**

- `src/MokiPad.Core/MarkdownEngine.cs`
- `src/MokiPad.Core/MarkdownCopy.cs`（为复制提供源码区间）

**调用链**

```text
MarkdownEngine.Parse
  → Markdig.Parse
  → 遍历 Block
  → PreviewBlock 列表
  → MarkdownResult
```

```csharp
private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
    .UsePipeTables()
    .UseTaskLists()
    .UseAutoLinks()
    .UseEmphasisExtras()
    .UseMathematics()
    .UseFootnotes()
    .Build();

public static MarkdownResult Parse(string source, CancellationToken cancellation = default)
{
    var watch = Stopwatch.StartNew();
    var doc = Markdown.Parse(source, Pipeline);
    var blocks = new List<PreviewBlock>();
    var anchors = new Dictionary<string, int>(StringComparer.Ordinal);

    foreach (var block in doc)
        Add(block);

    return new MarkdownResult(blocks, watch.Elapsed.TotalMilliseconds);
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 1-8 | `MarkdownPipeline` | 配置 Markdig 支持哪些语法；只构建一次，重复使用 |
| 3 | `UsePipeTables` | 支持 `|` 表格 |
| 4 | `UseTaskLists` | 支持 `- [x]` 任务列表 |
| 5 | `UseAutoLinks` | 自动识别网址 |
| 6 | `UseEmphasisExtras` | 支持 `~~`、`==`、`++`、`^^` 等扩展 |
| 7 | `UseMathematics` | 支持 `$...$` 和 `$$...$$` |
| 8 | `UseFootnotes` | 支持 `[^1]` 脚注 |
| 12 | `Markdown.Parse` | 把文本解析成 Markdig 的语法树 |
| 13 | `new List<PreviewBlock>()` | 输出块列表 |
| 14 | `anchors` | 标题锚点计数，处理重复标题 |
| 16-17 | `foreach ... Add(block)` | 逐个语法节点转成 `PreviewBlock` |
| 19 | `new MarkdownResult(...)` | 返回块列表和耗时 |

#### 3.3.1 块分发

```csharp
void Add(Block block, int depth = 0, string prefix = "")
{
    cancellation.ThrowIfCancellationRequested();
    switch (block)
    {
        case HeadingBlock h:
            var title = Plain(h.Inline);
            blocks.Add(new PreviewBlock("heading", h.Line + 1, h.Span.Start, title, h.Inline,
                h.Level, Anchor: UniqueAnchor(title)));
            break;
        case MathBlock math:
            var tex = math.Lines.ToString().Trim();
            if (tex.Length > 0)
                blocks.Add(new PreviewBlock("math", math.Line + 1, math.Span.Start, tex));
            break;
        case FencedCodeBlock code:
            AddChunks(code.Lines.ToString(), "code", code.Line + 1, code.Span.Start, code.Info ?? "");
            break;
        case Table table:
            // 表格转成行数组和单元格 Inline
            break;
        case ParagraphBlock paragraph:
            // 段落转成 paragraph 块，保留 Inline
            break;
    }
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 1 | `void Add(Block block, ...)` | 本地函数，递归处理子块 |
| 3 | `ThrowIfCancellationRequested` | 用户切走后尽快停止 |
| 4 | `switch (block)` | 按 Markdig 节点类型分派 |
| 6 | `case HeadingBlock h` | 标题节点 |
| 7 | `Plain(h.Inline)` | 把行内内容转成纯文本，用作标题和锚点 |
| 8-9 | `new PreviewBlock("heading", ...)` | 记录类型、行号、偏移、标题、Inline、级别、锚点 |
| 11 | `case MathBlock math` | 块级公式 |
| 12 | `math.Lines.ToString().Trim()` | 取出 TeX 文本 |
| 15 | `case FencedCodeBlock code` | 代码围栏 |
| 16 | `AddChunks(...)` | 大代码块按 4096 字符切分，避免单个控件过大 |
| 18 | `case Table table` | 表格，下面单独讲 |
| 23 | `case ParagraphBlock paragraph` | 普通段落 |

#### 3.3.2 锚点与纯文本

```csharp
static string UniqueAnchor(string title)
{
    var slug = Slugify(title);
    if (slug.Length == 0) slug = "section";
    if (!anchors.TryGetValue(slug, out var count))
    {
        anchors[slug] = 1;
        return slug;
    }

    anchors[slug] = ++count;
    return slug + "-" + count;
}

public static string Slugify(string text)
{
    var builder = new StringBuilder(string.Concat(text.ToLowerInvariant()
        .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or ' ')));
    return MyRegex().Replace(WhitespaceRegex().Replace(builder.ToString(), "-"), "-").Trim();
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3 | `Slugify(title)` | 把标题转成 URL 友好的锚点 |
| 4 | `slug.Length == 0` | 全是标点时给默认 `section` |
| 5-10 | `TryGetValue` | 第一次出现直接用；重复时加 `-2`、`-3` |
| 15 | `ToLowerInvariant` | 转小写，保证锚点稳定 |
| 16 | `Where(...)` | 只保留字母、数字、`_`、`-`、空格 |
| 17 | `WhitespaceRegex().Replace` | 连续空白变一个 `-` |
| 17 | `MyRegex().Replace` | 连续 `-` 合并，最后去掉首尾 |

---

### 3.4 阅读列表与滚动

**涉及文件**

- `src/MokiPad/Controls/MarkdownPreview.cs`
- `src/MokiPad/Controls/MarkdownSelection.cs`（partial，选择模块）
- `src/MokiPad/Styles/Controls.xaml`（滚动条样式）

**调用链**

```text
MarkdownPreview.SetBlocks
  → ItemsSource
  → ListBox 生成可见 ListBoxItem
  → BlockPresenter.Render
```

#### 3.4.1 虚拟化配置

```csharp
SetValue(ScrollViewer.CanContentScrollProperty, true);
SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
SetValue(VirtualizingPanel.ScrollUnitProperty, ScrollUnit.Pixel);
SetValue(VirtualizingPanel.CacheLengthProperty, new VirtualizationCacheLength(1));
ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 1 | `CanContentScroll=true` | 由列表项面板自己管理滚动，虚拟化需要 |
| 2 | `IsVirtualizing=true` | 只为可见区域附近创建控件 |
| 3 | `Recycling` | 滚出屏幕的容器回收复用 |
| 4 | `ScrollUnit.Pixel` | 按像素滚动，不是按项跳 |
| 5 | `CacheLength(1)` | 前后各缓存一页 |
| 6 | `VirtualizingStackPanel` | 纵向虚拟化堆叠面板 |

#### 3.4.2 滚轮与方向

```csharp
private void OnReadingWheel(object sender, MouseWheelEventArgs e)
{
    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 || e.Delta == 0) return;

    readingScroll ??= DescendantScroll(this);
    if (readingScroll is null) return;

    e.Handled = true;
    var direction = Math.Sign(e.Delta);

    if (!scrolling || direction != wheelDirection)
    {
        remainingScroll = 0;
        scrollRemainder = 0;
        if (scrolling) readingScroll.ScrollToVerticalOffset(readingScroll.VerticalOffset);
    }

    wheelDirection = direction;
    var lines = SystemParameters.WheelScrollLines;
    var distance = lines < 0
        ? readingScroll.ViewportHeight * .9
        : Math.Clamp(lines, 1, 8) * (double)GetValue(ReadingFontSizeProperty) * 1.75 * 1.25;

    remainingScroll = Math.Clamp(remainingScroll - e.Delta / 120.0 * distance,
        -readingScroll.VerticalOffset,
        Math.Max(0, readingScroll.ScrollableHeight - readingScroll.VerticalOffset));

    if (!SystemParameters.ClientAreaAnimation)
    {
        readingScroll.ScrollToVerticalOffset(ScrollTargetOffset);
        remainingScroll = 0;
        return;
    }

    if (scrolling) return;
    scrolling = true;
    lastScrollTick = Stopwatch.GetTimestamp();
    CompositionTarget.Rendering += AdvanceScroll;
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3 | `Ctrl` 或 `Delta == 0` | Ctrl 滚轮用于缩放，零增量无效 |
| 5 | `readingScroll ??=` | 第一次滚动时在可视树里找内部滚动条 |
| 9 | `e.Handled = true` | 阻止代码块内部滚动条再处理一次 |
| 10 | `direction` | 正数向上，负数向下 |
| 12-17 | 方向变化 | 清空剩余距离，立即停在当前位置，取消旧目标 |
| 19 | `wheelDirection` | 记录方向，供下一帧判断 |
| 20 | `WheelScrollLines` | 系统设置“一次滚几行”；小于 0 表示按页 |
| 21-23 | `distance` | 把行数换算成像素，随字号缩放 |
| 25-27 | `remainingScroll` | 累加/减少剩余距离，并限制在可滚动范围内 |
| 30-35 | 关闭动画 | 直接跳到目标并清空剩余距离 |
| 38-41 | 启动动画 | 记录时间，挂到每一帧的渲染事件 |

#### 3.4.3 每帧推进

```csharp
private void AdvanceScroll(object? sender, EventArgs e)
{
    if (readingScroll is null) { StopScrolling(); return; }

    var now = Stopwatch.GetTimestamp();
    var seconds = Math.Clamp(Stopwatch.GetElapsedTime(lastScrollTick, now).TotalSeconds, 0, 1.0 / 60);
    lastScrollTick = now;

    remainingScroll = Math.Clamp(remainingScroll,
        -readingScroll.VerticalOffset,
        Math.Max(0, readingScroll.ScrollableHeight - readingScroll.VerticalOffset));

    var difference = remainingScroll;
    var completing = Math.Abs(difference) < .5;
    var frameLimit = Math.Clamp(readingScroll.ViewportHeight * .12, 48, 96);
    var step = Math.Clamp(completing ? difference : difference * (1 - Math.Exp(-seconds / .045)),
        -frameLimit, frameLimit);

    remainingScroll -= step;
    readingScroll.ScrollToVerticalOffset(readingScroll.VerticalOffset + step);
    if (completing) StopScrolling();
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3 | `readingScroll is null` | 控件卸载后停止 |
| 5-6 | `seconds` | 计算两帧间隔，最大按 1/60 秒，避免卡顿后一次追太多 |
| 9-11 | `remainingScroll` | 再次限制在可滚动范围 |
| 13 | `difference` | 还剩多少像素 |
| 14 | `completing` | 小于半像素就认为完成 |
| 15 | `frameLimit` | 单帧最多走视口 12%，限制 48–96 像素，防止突跳 |
| 16-17 | `step` | 指数平滑；越接近目标走得越慢 |
| 19 | `remainingScroll -= step` | 扣掉本帧已经走的距离 |
| 20 | `ScrollToVerticalOffset` | 真正移动滚动条 |
| 21 | `StopScrolling` | 到达目标，解绑渲染事件 |

#### 3.4.4 跳转与高亮

```csharp
private void ScrollToIndex(int index)
{
    if (index < 0 || index >= Items.Count || Items[index] is not { } item) return;
    ScrollIntoView(item);
    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
    {
        if (ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) return;
        var accent = WpfApp.Current.FindResource("Accent") as SolidColorBrush ?? Brushes.Transparent;
        container.Background = new SolidColorBrush(Color.FromArgb(46, accent.Color.R, accent.Color.G, accent.Color.B));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        timer.Tick += (_, _) => { timer.Stop(); container.Background = Brushes.Transparent; };
        timer.Start();
    }));
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3 | 边界判断 | 索引越界或数据为空就不做 |
| 4 | `ScrollIntoView(item)` | 让列表滚动到该项；虚拟化列表也支持 |
| 5 | `DispatcherPriority.Loaded` | 等容器生成后再高亮 |
| 7 | `ContainerFromIndex` | 取该项对应的 `ListBoxItem` |
| 8 | `FindResource("Accent")` | 从应用资源取主题色 |
| 9 | `Color.FromArgb(46, ...)` | 半透明强调色，46 是透明度 |
| 10-12 | `DispatcherTimer` | 1.2 秒后恢复透明，做一个短暂提示 |

**本模块其余方法**

| 方法 | 作用 |
| --- | --- |
| `SetBlocks` | 替换块列表 |
| `Navigate` | 滚动到指定块 |
| `ScrollToAnchor` | 按标题锚点跳转 |
| `ScrollToFootnote` / `ScrollToFootnoteReference` | 脚注与引用互跳 |
| `ResolvePath` | 相对链接转绝对路径 |
| `DescendantScroll` | 在可视树里找滚动条 |
| `StopScrolling` | 停止动画并解绑渲染事件 |
| `MarkdownSelection` 中的选择方法 | 见 3.10 |

---

### 3.5 块渲染

**涉及文件**

- `src/MokiPad/Controls/MarkdownPreview.cs`（`BlockPresenter` 类）
- `src/MokiPad/Rendering/MathView.cs`（公式块）
- `src/MokiPad/Styles/Controls.xaml`（按钮样式）

**调用链**

```text
ListBox 创建 ListBoxItem
  → ItemTemplate 创建 BlockPresenter
  → Block 依赖属性变化
  → Render()
```

```csharp
internal void Render()
{
    if (Block is not { } block)
    {
        Content = null;
        return;
    }

    Margin = new Thickness(32 + Math.Min(block.Level, 8) * (block.Kind == "paragraph" ? 15 : 0),
        0, 32, block.Checked.HasValue || block.Prefix.Length > 0 ? 4 : 16);
    ToolTip = null;

    switch (block.Kind)
    {
        case "heading":
            Margin = new Thickness(32, block.Line == 1 ? 0 : 20, 32, 12);
            var heading = Text("", block.Level switch { 1 => 33.6, 2 => 24.8, 3 => 20.8, 4 => 17.92, _ => 16 });
            if (block.Inline is not null) Inline(block.Inline, heading.Inlines, heading.FontSize);
            else heading.Text = block.Text;
            // 标题图标、字号、行高、DockPanel 组合
            Content = row;
            break;
        case "rule":
            Content = new Border
                { Height = 1, Background = Brush("MdLine"), Margin = new Thickness(0, 12, 0, 12) };
            break;
        case "math":
            Content = new MathView
            {
                Source = block.Text, Display = true, MathFontSize = 17 * ReadingScale,
                MathForeground = Brush("Text"), HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 10)
            };
            break;
        case "code":
            // 完整代码见 3.5.2；这里省略工具栏和 ScrollViewer
            Content = codeBorder;
            break;
        case "table":
            Content = Table(block);
            break;
        default:
            // 段落、引用、任务列表：见 3.5.3 之后的默认分支
            Content = container;
            break;
    }
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3-7 | `Block is not { } block` | 没有数据就清空内容 |
| 9-10 | `Margin = ...` | 统一左右 32 像素；标题/列表按级别缩进 |
| 11 | `ToolTip = null` | 清掉上一个块可能留下的提示 |
| 13 | `switch (block.Kind)` | 按块类型选择渲染方式 |
| 15-22 | `case "heading"` | 标题：单独设置上下边距，生成 `TextBlock` |
| 24-26 | `case "rule"` | 分隔线：1 像素高的 `Border` |
| 27-35 | `case "math"` | 公式：创建 `MathView` 并居中 |
| 36-39 | `case "code"` | 代码块：工具栏 + 等宽文本 + 横向滚动 |
| 40-42 | `case "table"` | 表格：交给 `Table` 方法 |
| 43-46 | `default` | 普通段落、引用、任务列表 |

> 这里省略了标题图标、代码工具栏、段落复选框等大段细节，后面按分支继续讲；完整代码在 `MarkdownPreview.cs` 的 `Render()` 里。

#### 3.5.1 标题分支

```csharp
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
            // 创建笔记本图标并放到左边
            break;
        case 2:
            var hash = Text("#", heading.FontSize);
            // 设置 # 的颜色、边距并放到左边
            break;
    }

    row.Children.Add(heading);
    Content = row;
    break;
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 2 | `Margin = ...` | 第一行标题不加上边距，其他标题加 20 |
| 3-4 | `Text("", ...)` | 创建空 `TextBlock`，字号按标题级别 |
| 5-6 | `Inline(...)` | 把 Markdown 行内节点转成 WPF `Inline` |
| 7 | `heading.Text` | 没有行内节点时直接显示纯文本 |
| 8 | `FontWeight` | 半粗体 |
| 9 | `LineHeight` | 行高为字号 1.5 倍 |
| 10 | `Foreground` | 五级、六级标题用次要颜色 |
| 11 | `new DockPanel()` | 标题行：左边图标，右边文字 |
| 12-23 | `switch (block.Level)` | 一级放笔记本图标，二级放 `#` |
| 25-26 | `row.Children.Add(heading)` | 文字填满剩余区域 |

#### 3.5.2 代码块

```csharp
var panel = new DockPanel();
var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

var copy = new Button
{
    Style = WpfApp.Current.FindResource("IconButton") as Style,
    Content = WpfApp.Current.FindResource("Icon.Copy"),
    ToolTip = UiText.Get("CopyCode")
};
copy.Click += (_, _) => Clipboard.SetText(block.CodeSource ?? block.Text);
DockPanel.SetDock(copy, Dock.Right);
bar.Children.Add(copy);

var label = Text(block.Language, 12);
label.Foreground = Brush("Secondary");
bar.Children.Add(label);

var code = Text("", 14);
code.FontFamily = AppFonts.Code;
code.TextWrapping = TextWrapping.NoWrap;
ColorCode(block.Text.TrimEnd('\r', '\n'), block.Language, code.Inlines);

panel.Children.Add(bar);
panel.Children.Add(new ScrollViewer
{
    Content = code,
    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
});
Content = new Border { Child = panel, Background = Brush("MdCode"), ... };
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 1 | `DockPanel` | 代码块整体：上面工具栏，下面代码 |
| 4-9 | `Button` | 复制按钮；样式来自资源 |
| 10 | `Clipboard.SetText` | 复制完整围栏源码或当前文本 |
| 11 | `DockPanel.SetDock(copy, Dock.Right)` | 按钮靠右 |
| 14-16 | `label` | 显示语言名 |
| 18-21 | `Text("", 14)` | 代码文本控件，使用等宽字体 |
| 22 | `ColorCode(...)` | 词法着色，把不同片段做成不同颜色的 `Run` |
| 24-29 | `ScrollViewer` | 代码太长时横向滚动 |
| 30 | `Border` | 圆角、背景、边框 |

#### 3.5.3 表格

```csharp
var grid = new Grid();
for (var c = 0; c < columns; c++)
    grid.ColumnDefinitions.Add(new ColumnDefinition());

for (var r = 0; r < rows.Length; r++)
{
    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    for (var c = 0; c < rows[r].Length; c++)
    {
        var text = Text("", 15);
        // 填单元格内容
        var cell = new Border { Child = text, Padding = new Thickness(12, 7, 12, 7) };
        Grid.SetRow(cell, r);
        Grid.SetColumn(cell, c);
        grid.Children.Add(cell);
    }
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 1 | `new Grid()` | 表格本体 |
| 2-3 | `ColumnDefinition` | 每一列一个定义 |
| 5-7 | `RowDefinition` | 每一行高度按内容自动 |
| 9-10 | 单元格 `TextBlock` | 每个单元格独立文本控件 |
| 13 | `Border` | 单元格边框和内边距 |
| 14-15 | `Grid.SetRow/SetColumn` | 把单元格放到对应行列 |
| 16 | `grid.Children.Add(cell)` | 加入表格 |

**本模块其余方法**

| 方法 | 作用 |
| --- | --- |
| `Text` | 创建统一样式的 `TextBlock` |
| `Inline` | Markdig 行内节点 → WPF Inline |
| `HtmlBlock` | 原始 HTML → `StackPanel` |
| `Table` | 表格 → `Border + Grid` |
| `ColorCode` | 代码词法着色 |
| `ImageInline` | 图片 → `InlineUIContainer` |

---

### 3.6 HTML 行内

**涉及文件**

- `src/MokiPad/Controls/HtmlBlocks.cs`
- `src/MokiPad/Controls/MarkdownPreview.cs`（`HtmlContext`）

**调用链**

```text
BlockPresenter.Render
  → HtmlBlock / Inline
  → HtmlContext.ApplyTag
  → InlineCollection
```

```csharp
public void AddText(string text)
{
    if (skip || text.Length == 0) return;
    var value = WebUtility.HtmlDecode(text);
    if (value.Length == 0) return;

    var run = new Run(value);
    if (style.Bold) run.FontWeight = FontWeights.Bold;
    if (style.Italic) run.FontStyle = FontStyles.Italic;
    if (style.Code || style.Kbd)
    {
        run.FontFamily = AppFonts.Code;
        run.Background = WpfApp.Current.FindResource("MdSoft") as Brush;
        run.FontSize = FontSize * .88;
    }

    Target.Add(run);
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3 | `skip` | 在 `<script>`/`<style>` 内时直接忽略 |
| 4 | `HtmlDecode` | 把 `&amp;` 等实体还原成字符 |
| 8 | `new Run(value)` | WPF 的文本片段 |
| 9-10 | 加粗/斜体 | 根据当前样式设置 |
| 11-16 | 代码/键盘样式 | 等宽字体、浅色背景、缩小字号 |
| 18 | `Target.Add(run)` | 加入当前 `InlineCollection` |

```csharp
public void ApplyTag(string raw, BlockPresenter host, double fontSize)
{
    var tag = raw.Trim();
    if (tag.Length < 2 || tag.StartsWith("<!", StringComparison.Ordinal)) return;

    var closing = tag.StartsWith("</", StringComparison.Ordinal);
    var body = tag.Trim('<', '>', ' ').TrimStart('/');
    var name = body.Split(' ', '\t', '\r', '\n', '/')[0].ToLowerInvariant();

    if (skip && name is not ("script" or "style")) return;

    switch (name)
    {
        case "br":
        case "hr":
            if (!closing) Target.Add(new LineBreak());
            return;
        case "a":
            // 开始链接：压栈并切换 Target
            // 结束链接：出栈
            return;
        case "b":
        case "strong":
            PushStyle(closing);
            style.Bold = !closing;
            return;
    }
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3-4 | 去掉空白、忽略注释 | `<!--` 不是可显示的标签 |
| 6 | `closing` | 判断 `</tag>` |
| 7 | `body` | 去掉尖括号和斜杠 |
| 8 | `name` | 取标签名，统一小写 |
| 10 | `skip` | script/style 内部不显示 |
| 12 | `switch (name)` | 按标签名处理 |
| 14-17 | `br`/`hr` | 插入换行 |
| 18-23 | `a` | 链接：切换当前目标集合 |
| 24-27 | `b`/`strong` | 加粗：压栈/出栈 |

**本模块其余方法**

| 方法 | 作用 |
| --- | --- |
| `PushStyle` | 开始标签复制样式，结束标签恢复 |
| `AppendFragment` | 把 HTML 文本按标签切开，分别处理 |
| `Attribute` | 读取 `href`、`src`、`width` |
| `HtmlBlocks.Parse` | 把块级 HTML 切成段落/标题/列表 |

---

### 3.7 图片与 SVG

**涉及文件**

- `src/MokiPad/Controls/ImageLoader.cs`
- `src/MokiPad/Controls/BoundedImageCache.cs`
- `src/MokiPad/Controls/MarkdownPreview.cs`（`ImageInline`、`LoadImageAsync`）

**调用链**

```text
BlockPresenter.ImageInline
  → ImageLoader.LoadAsync / ReadBytesAsync
    → BoundedImageCache.GetAsync
      → 本地/网络/data URI
      → SVG: SvgWorker
      → 位图: BitmapImage
```

```csharp
public static Task<ImageSource?> LoadAsync(string source, string? documentPath, int pixelWidth = 0)
{
    var key = Key(source, documentPath);
    var width = pixelWidth <= 0 ? 0 : (int)Math.Ceiling(pixelWidth / 256.0) * 256;

    return key is null
        ? Task.FromResult<ImageSource?>(null)
        : Cache.GetAsync(key + ":" + width,
            () => Task.Run(() => LoadCoreAsync(source, documentPath, width)),
            image => image is BitmapSource bitmap
                ? (long)bitmap.PixelWidth * bitmap.PixelHeight * 4
                : 64 * 1024);
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3 | `Key(...)` | 生成缓存键；本地文件带修改时间 |
| 4 | `width` | 按 256 像素分档，避免窗口每变一点就重新解码 |
| 6-7 | `key is null` | 地址无效直接返回空 |
| 8-11 | `Cache.GetAsync` | 有界缓存；同一个键共享同一个任务 |
| 9 | `Task.Run` | 读取和解码放到线程池 |
| 10-11 | 权重 | 位图按像素估算内存，SVG 按固定值 |

```csharp
private static BitmapImage? DecodeBitmap(byte[] bytes, int requestedWidth)
{
    int width;
    using (var probe = new MemoryStream(bytes))
    {
        var decoder = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        width = decoder.Frames[0].PixelWidth;
    }

    using var stream = new MemoryStream(bytes);
    var image = new BitmapImage();
    image.BeginInit();
    image.CacheOption = BitmapCacheOption.OnLoad;
    image.DecodePixelWidth = requestedWidth > 0 && width > requestedWidth ? requestedWidth : 0;
    image.StreamSource = stream;
    image.EndInit();
    image.Freeze();
    return image;
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3-7 | 探测宽度 | 先用轻量解码器读原始像素宽度 |
| 9 | 新建流 | 上一次探测已经把流读完，需要重新打开 |
| 11-12 | `BeginInit` | 开始批量设置位图属性 |
| 13 | `OnLoad` | 解码后立即把像素读进内存 |
| 14 | `DecodePixelWidth` | 只解码到需要宽度，省内存 |
| 15 | `StreamSource` | 指定位图数据 |
| 16 | `EndInit` | 结束设置并真正解码 |
| 17 | `Freeze` | 冻结成只读对象，可跨线程、可优化 |

**本模块其余方法**

| 方法 | 作用 |
| --- | --- |
| `LocalPath` | 相对/绝对/file URI 转本地路径 |
| `Key` | 缓存键 |
| `ReadBytesAsync` | 原始字节缓存 |
| `ReadBytesCoreAsync` | data URI、网络下载、重试、磁盘缓存 |
| `LooksLikeSvg` | 先看文件头，避免把带 svg 字符串的 PNG 当 SVG |
| `DecodeSvg` | SharpVectors 转 `DrawingImage` |
| `InlineNestedSvgImages` | 展开徽章内嵌 SVG |
| `DecodeBitmap` | 位图解码 |
| `BoundedImageCache.GetAsync` | 异步去重、预算淘汰 |

---

### 3.8 GIF 播放

**涉及文件**

- `src/MokiPad/Controls/GifAnimation.cs`

**调用链**

```text
ImageInline
  → LoadImageAsync
    → IsGif
    → new GifAnimation
    → StartAsync
      → 后台 SkiaSharp 解码
      → Dispatcher 写入 WriteableBitmap
```

```csharp
private async Task DecodeAsync()
{
    var token = cancellation.Token;
    using var data = SKData.CreateCopy(bytes);
    using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Invalid GIF");

    FrameCount = Math.Max(1, codec.FrameCount);
    var width = codec.Info.Width;
    var height = codec.Info.Height;

    var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
    using var canvas = new SKBitmap(info);
    canvas.Erase(SKColors.Transparent);

    await target.Dispatcher.InvokeAsync(() =>
    {
        surface = new WriteableBitmap(width, height, 96, 96, PixelFormats.Pbgra32, null);
        target.Source = surface;
        RenderOptions.SetBitmapScalingMode(target, BitmapScalingMode.HighQuality);
    }, DispatcherPriority.Background, token);

    while (!token.IsCancellationRequested)
    {
        var result = codec.GetPixels(info, canvas.GetPixels(), canvas.RowBytes,
            new SKCodecOptions(index, prior));
        // 写入同一张 surface
        await Task.Delay(delay, token);
    }
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3 | `SKData.CreateCopy(bytes)` | 把字节交给 SkiaSharp |
| 4 | `SKCodec.Create` | 创建 GIF 解码器 |
| 6-8 | 宽高 | 原始像素尺寸 |
| 10 | `SKImageInfo` | 指定 BGRA 像素格式 |
| 11-12 | 画布 | 一帧的像素缓冲区 |
| 14-19 | `Dispatcher.InvokeAsync` | 创建 WPF 位图属于界面线程操作 |
| 15 | `WriteableBitmap` | 可写位图，后续只更新像素不换控件 |
| 20-22 | 循环解码 | 每帧解出像素 |
| 23 | `GetPixels` | 解码到画布 |
| 25+ | 写位图/延时 | 控制帧率 |

**本模块重点**

- 同一张 `WriteableBitmap` 更新像素，避免每帧触发布局。
- 解码在后台线程，写入在界面线程。
- 离开视口暂停，卸载时释放。
- 帧数、当前帧、播放任务都有明确生命周期。

---

### 3.9 数学公式

**涉及文件**

- `src/MokiPad/Rendering/MathView.cs`

**调用链**

```text
BlockPresenter
  → new MathView { Source, Display, MathFontSize }
  → Rebuild()
  → WpfTeXFormulaParser.Parse
  → FormulaControl
```

```csharp
private void Rebuild()
{
    if (string.IsNullOrWhiteSpace(source))
    {
        Content = null;
        HasError = false;
        return;
    }

    try
    {
        WpfTeXFormulaParser.Instance.Parse(source);
        formula.Foreground = mathForeground;
        formula.Scale = Scale;
        formula.Formula = source;
        HasError = formula.HasError;
        Content = HasError ? Fallback() : formula;
    }
    catch (Exception ex) when (ex is TexException or ArgumentException or InvalidOperationException)
    {
        HasError = true;
        Content = Fallback();
    }
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3-7 | 空源码 | 清空内容，不显示错误 |
| 12 | `Parse(source)` | 先自己解析一次，提前捕获非法 TeX |
| 13 | `Foreground` | 公式颜色跟随主题 |
| 14 | `Scale` | 字号缩放，行间公式更大 |
| 15 | `Formula` | 真正交给 FormulaControl 排版 |
| 16 | `HasError` | 库自己报告的错误 |
| 17 | `Content` | 成功显示公式，失败显示原文 |
| 19-23 | `catch` | 捕获 TeX 异常，回退为文本 |

**本模块重点**

- WpfMath 负责解析和排版，MokiPad 只做宿主。
- 失败时显示原始 TeX，不让公式区域空白。
- `Source` / `Display` 是 `init`，只能对象初始化时设置。

---

### 3.10 选择与复制

**涉及文件**

- `src/MokiPad/Controls/MarkdownSelection.cs`
- `src/MokiPad.Core/MarkdownCopy.cs`
- `src/MokiPad/Services/MarkdownClipboard.cs`

**调用链**

```text
鼠标按下/移动/抬起
  → BeginSelection / ExtendDrag
  → HitSelection 找到块和源码偏移
  → SelectRange
  → GetSelectedMarkdown
  → MarkdownCopy.Selection
  → MarkdownClipboard.Write
```

```csharp
public static string Selection(string source, IReadOnlyList<PreviewBlock> blocks,
    ReadingPosition anchor, ReadingPosition focus, bool all = false)
{
    if (all) return source;
    var (start, end) = Compare(anchor, focus) <= 0 ? (anchor, focus) : (focus, anchor);
    if (start == end || blocks.Count == 0) return "";

    var pieces = new List<(PreviewBlock Block, SourceRange Range)>();
    for (var index = Math.Max(0, start.Block); index <= Math.Min(end.Block, blocks.Count - 1); index++)
    {
        var block = blocks[index];
        var range = Range(block);
        if (!range.Valid || range.End > source.Length) continue;

        if (!IsAtomic(block))
            range = new(Math.Max(range.Start, index == start.Block ? start.Offset : range.Start),
                Math.Min(range.End, index == end.Block ? end.Offset : range.End));

        if (!range.Valid) continue;
        if (pieces.Count > 0 && pieces[^1].Range == range) continue;
        pieces.Add((block, range));
    }

    var result = new StringBuilder();
    // 拼接片段，并补齐引用式链接/脚注定义
    return result.ToString();
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3 | `all` | Ctrl+A 时直接返回整篇源码 |
| 4 | `Compare` | 保证 start 在 end 前面，支持反向拖选 |
| 5 | 空选区 | 没有选择就返回空字符串 |
| 7-8 | 遍历块 | 只处理选区覆盖的块 |
| 10-11 | `Range(block)` | 从块里取源码起止 |
| 13-15 | `IsAtomic` | 图片、表格、代码块等作为整体复制，不切半截 |
| 17-18 | 去重 | 长代码块/表格可能被拆成多个虚拟块，避免重复 |
| 21 | `StringBuilder` | 拼接最终 Markdown |
| 23+ | 定义补齐 | 引用式链接、脚注要把定义一起复制 |

**本模块重点**

- 复制的是**原始 Markdown**，不是屏幕文字。
- 选区用“块索引 + 块内偏移”记录，容器回收不影响。
- 原子块（图片/表格/代码）保持完整语法。
- 引用式链接和脚注需要额外补定义，否则复制出去不可用。

---

### 3.11 主题、样式与字体

**涉及文件**

- `src/MokiPad/Services/ThemeService.cs`
- `src/MokiPad/Styles/Controls.xaml`
- `src/MokiPad/Services/AppFonts.cs`
- `src/MokiPad/App.xaml`

**调用链**

```text
ThemeService.Apply
  → Application.Current.Resources[name] = brush
  → DynamicResource 重新取画刷
```

```csharp
public static void Apply(bool dark, bool animate = false)
{
    var values = new Dictionary<string, string>
    {
        ["Surface"] = dark ? "#1E1E1E" : "#FFFFFF",
        ["Text"] = dark ? "#F5F5F7" : "#2C2C2E",
        ["Accent"] = dark ? "#5A9FFF" : "#4091FF"
    };

    foreach (var (name, value) in values)
    {
        var color = (Color)ColorConverter.ConvertFromString(value);
        var previous = Application.Current.Resources[name] as SolidColorBrush;
        var brush = new SolidColorBrush(animate && previous is not null ? previous.Color : color);

        if (animate && previous is not null && SystemParameters.ClientAreaAnimation)
        {
            brush.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(color, TimeSpan.FromMilliseconds(220)));
        }
        else
        {
            brush.Color = color;
            brush.Freeze();
        }

        Application.Current.Resources[name] = brush;
    }
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3-7 | 颜色表 | 颜色名与 XAML 里的 `DynamicResource` 对应 |
| 9 | `ColorConverter` | 把 `#RRGGBB` 转成 `Color` |
| 10 | `previous` | 取旧画刷，动画从旧颜色开始 |
| 11 | `new SolidColorBrush` | 创建新画刷 |
| 13-17 | 动画分支 | 从旧色渐变到新色 |
| 18-22 | 静态分支 | 直接设置并冻结，性能更好 |
| 24 | 替换资源 | 所有 `DynamicResource` 自动更新 |

**本模块重点**

- `StaticResource` 只查一次；`DynamicResource` 会跟随资源变化。
- 冻结的画刷不能动画；动画分支不调用 `Freeze`。
- 字体通过 `pack://` 地址打包，避免依赖系统安装。
- 样式集中在 `Controls.xaml`，窗口只引用资源键。

---

### 3.12 设置与本地化

**涉及文件**

- `src/MokiPad/Services/Preferences.cs`
- `src/MokiPad/Services/UiText.cs`

**调用链**

```text
MainWindow 构造
  → Preferences.Load
  → UiText.Apply
  → 界面文案/主题
```

```csharp
public sealed class Preferences
{
    public bool Dark { get; set; }
    public string Language { get; set; } = "zh";
    public double FontSize { get; set; } = 16;

    public static Preferences Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsFile))
                ?? new Preferences();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Preferences();
        }
    }
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3 | `Dark` | 是否深色主题 |
| 4 | `Language` | 界面语言 |
| 5 | `FontSize` | 阅读字号 |
| 9-10 | `Load` | 读 JSON；文件不存在或损坏时返回默认值 |
| 11 | `Deserialize` | 反序列化 |
| 13 | `?? new Preferences()` | 空文件时用默认设置 |
| 15 | `catch` | IO/JSON/权限错误都回退默认 |

`UiText` 用字典保存中英文文案；`Get(key)` 根据当前语言取文本，找不到时返回键名，方便发现漏翻。

---

### 3.13 菜单、快捷键、背景

**涉及文件**

- `src/MokiPad/MainWindow.xaml.cs`
- `src/MokiPad/MainWindow.xaml`

**调用链**

```text
窗口初始化
  → BindKeys
  → 菜单按钮 Click
  → CreateFileMenu / CreateSettingsMenu / CreateReaderMenu
```

```csharp
private void BindKeys()
{
    Bind(Key.O, ModifierKeys.Control, () => OnOpen(this, new RoutedEventArgs()));
    Bind(Key.W, ModifierKeys.Control, () => CloseTab(active));
    Bind(Key.F5, ModifierKeys.None, () => _ = ReloadAsync());
    Bind(Key.F11, ModifierKeys.None, () => OnMaximize(this, new RoutedEventArgs()));

    return;

    void Bind(Key key, ModifierKeys mods, Action action)
    {
        var command = new RoutedCommand();
        CommandBindings.Add(new CommandBinding(command, (_, _) => action()));
        InputBindings.Add(new KeyBinding(command, key, mods));
    }
}
```

| 行 | 代码 | 解释 |
| --- | --- | --- |
| 3 | `Ctrl+O` | 打开文件 |
| 4 | `Ctrl+W` | 关闭当前标签 |
| 5 | `F5` | 重新加载 |
| 6 | `F11` | 最大化/还原 |
| 8 | `return;` | 分隔主流程和本地函数，本地函数不会被执行 |
| 10-13 | `Bind` | 创建命令、绑定执行动作、绑定按键 |

**本模块重点**

- 快捷键用 `RoutedCommand + CommandBinding + KeyBinding`。
- 菜单是代码动态创建的，不是 XAML 写死的。
- 背景图用 `BitmapImage`，设置 `DecodePixelWidth` 限制解码宽度。
- 窗口用 `WindowChrome` 自定义标题栏，按钮要设置 `IsHitTestVisibleInChrome`。

---

### 3.14 验证

**涉及文件**

- `tests/MokiPad.Verification/Program.cs`
- `tests/MokiPad.Verification/MarkdownCopyVerification.cs`
- `tests/MokiPad.Verification/ScrollReplay.cs`
- `tests/MokiPad.Verification/NativeScrollReplay.cs`

**验证内容**

| 文件 | 检查 |
| --- | --- |
| `Program.cs` | 主题图标、字体、菜单、组件画廊、公式、图片、HTML、链接、脚注、长文虚拟化、代码块滚轮 |
| `MarkdownCopyVerification.cs` | 选择、复制、原子块、引用定义 |
| `ScrollReplay.cs` | 逐帧滚轮回放、方向反转、单帧位移 |
| `NativeScrollReplay.cs` | 真实 WPF 渲染队列、布局取整、阻塞 UI 后的追赶 |

**运行方式**

```powershell
.\scripts\verify.ps1
```

验证程序创建离屏窗口，不弹出真实窗口；报告写入 `artifacts/verification.json`。

---

## 4. 功能到文件速查

| 想改什么 | 先看 |
| --- | --- |
| 窗口布局、标签栏、标题栏 | `MainWindow.xaml` |
| 打开文件、切换标签、渲染流程 | `MainWindow.xaml.cs` |
| Markdown 语法支持 | `MarkdownEngine.cs` |
| 阅读列表、滚动、跳转 | `MarkdownPreview.cs` |
| 单个块长什么样 | `MarkdownPreview.cs` 的 `BlockPresenter.Render` |
| HTML 标签支持 | `MarkdownPreview.cs` 的 `HtmlContext`、`HtmlBlocks.cs` |
| 图片/网络/SVG | `ImageLoader.cs` |
| GIF 播放 | `GifAnimation.cs` |
| 数学公式 | `MathView.cs` |
| 选择复制 | `MarkdownSelection.cs`、`MarkdownCopy.cs` |
| 主题颜色 | `ThemeService.cs` |
| 控件样式 | `Styles/Controls.xaml` |
| 字体 | `AppFonts.cs`、`Assets/Fonts` |
| 设置保存 | `Preferences.cs` |
| 中英文文案 | `UiText.cs` |
| 验证 | `tests/MokiPad.Verification` |

---

## 5. 建议的阅读顺序

1. 先看 `App.xaml.cs` 和 `MainWindow.xaml`，知道窗口怎么出现；
2. 看 `FileService.cs` 和 `MarkdownEngine.cs`，知道文件怎么变成块；
3. 看 `MarkdownPreview.cs` 的构造函数和 `SetBlocks`，知道块怎么进入列表；
4. 看 `BlockPresenter.Render`，知道每种块怎么画；
5. 按兴趣看图片、GIF、公式、复制、主题；
6. 最后看验证项目，知道怎么证明没改坏。

---

## 6. 和 mgtree 的对应

本文配套 `docs/code-walkthrough.mgtree`：

- 一级标题对应根节点；
- `## 3. 模块逐行讲解` 下的每个模块对应二级节点；
- 每个模块里的代码块用 `>csharp[n]`、`>xml[n]`、`>powershell[n]` 标注；
- 序号按语言分别计数，从 1 开始。

例如：

```tree
MokiPad 代码导读
  3. 模块逐行讲解
    3.1 应用启动与窗口壳
      App 入口 >csharp[1]
      主窗口构造 >csharp[2]
```

用 MokiDown 打开 `.mgtree` 后，点节点可以直接跳到对应代码块。
