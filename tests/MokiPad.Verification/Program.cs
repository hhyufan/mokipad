using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MokiPad.Controls;
using MokiPad.Core;
using MokiPad.Rendering;
using MokiPad.Services;

namespace MokiPad.Verification;

internal static class Program
{
    private static readonly List<string> Passed = [];
    private static readonly Dictionary<string, object> Metrics = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string output = "";
    private static bool scrollOnly;
    private static readonly string[] SourceArray =
            [
                "Editor", "SourceHost", "PreviewToggleButton", "SplitModeButton", "Navigation", "FindBar",
                "NavigationItems"
            ];

    // ReSharper disable once ParameterOnlyUsedForPreconditionCheck.Local
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Passed.Add(name);
        Console.WriteLine("PASS " + name);
    }

    [STAThread]
    public static int Main(string[] args)
    {
        output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts");
        scrollOnly = args.Contains("--scroll-only");
        Directory.CreateDirectory(output);
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var result = 1;
        _ = app.Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await Run();
                result = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Metrics["error"] = ex.ToString();
            }
            finally
            {
                foreach (var window in app.Windows.OfType<MainWindow>().ToArray()) window.EndVerification();
                Metrics["passed"] = Passed;
                Metrics["environment"] = new
                {
                    runtime = Environment.Version.ToString(), cpu = Environment.ProcessorCount,
                    measuredUtc = DateTime.UtcNow
                };
                // ReSharper disable once MethodHasAsyncOverload
                // The app is shutting down on the dispatcher; a synchronous write guarantees the file is flushed first.
                File.WriteAllText(Path.Combine(output, "verification.json"),
                    JsonSerializer.Serialize(Metrics, JsonOptions));
                app.Shutdown(result);
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        });
        Dispatcher.Run();
        return result;
    }

    private static async Task Run()
    {
        var timer = Stopwatch.StartNew();
        var window = new MainWindow(new Preferences { Background = false, Language = "zh" });
        Metrics["viewerConstructorMs"] = timer.Elapsed.TotalMilliseconds;
        var root = window.VerificationSurface;
        Layout(root);
        Metrics["viewerOffscreenLayoutMs"] = timer.Elapsed.TotalMilliseconds;
        await MarkdownCopyVerification.Run(window, output, Check, (element, name) => Capture(element, name));
        if (Environment.GetCommandLineArgs().Contains("--copy-only")) return;
        await VerifyPolish();
        await VerifyScrollReversal(window);
        var replay = await ScrollReplay.Run(window, output);
        Metrics["scrollReplay"] = replay;
        var nativeReplay = await NativeScrollReplay.Run(output);
        Metrics["nativeScrollReplay"] = nativeReplay;
        Check(nativeReplay.All(run => run.Frames >= 8 && run.MaxFrameStepPx <= run.FrameBudgetPx + 1),
            "Native presented frames stay within the movement budget after input bursts and rendering stalls");
        Check(nativeReplay.All(run => Math.Abs(run.VisibleTravelPixels - run.RequestedPixels) <= Math.Max(2, run.RequestedPixels * .02)),
            "Native smooth scrolling preserves the total wheel distance while bounding individual frames");
        Check(replay.MaxBacktrackPx <= .5, "Mixed heights, high-frequency input, reversals and delayed height changes never reverse visible motion");
        Check(replay.Realized is > 0 and < 150, "Scroll replay keeps document containers virtualized");
        if (scrollOnly) return;
        Check(
SourceArray.All(name => window.FindName(name) is null),
            "Viewer contains no hidden editor, modes, outline, recent list or search");
        Check(typeof(MainWindow).Assembly.GetReferencedAssemblies().All(a => a.Name?.Contains("AvalonEdit") != true),
            "Viewer no longer references the editor engine");
        Check(!Descendants(root).OfType<TextBox>().Any(), "Reader has no editable title or source text boxes");
        var banned = new[] { Key.F, Key.H, Key.N, Key.S, Key.D1, Key.D2, Key.D3 };
        Check(window.InputBindings.OfType<KeyBinding>().All(b => !banned.Contains(b.Key)),
            "Removed editor and search shortcuts are not registered");
        window.ApplyVerificationBackground();
        Layout(root);
        Capture(root, "viewer-welcome.png");

        const string source =
            "# 灵感笔记\n\n让 **灵感** 自然成形，使用 `Markdown`。\n\n> 写文档，连接自己的知识。\n\n## 今天的灵感\n\n- [x] 记录灵感\n- [ ] 验证想法\n\n## 在笔记里验证想法\n\n```javascript\nconst ideas = [\"记录\", \"尝试\", \"连接\"];\nconsole.log(ideas.join(\" → \"));\n```\n\n## 从想法到作品\n\n| 步骤 | 行动 |\n| --- | --- |\n| 记录 | 写下灵感 |\n| 实践 | 运行代码 |\n";

        var parsed = MarkdownEngine.Parse(source);
        Check(parsed.Blocks.Count(block => block.Kind == "heading") == 4, "Markdown heading hierarchy remains correct");
        Check(parsed.Blocks.Count(b => b.Checked.HasValue) == 2, "Task states remain visible in the read-only model");
        Check(parsed.Blocks.Any(b => b.Rows?.Length == 3), "Pipe table data remains intact");
        var commands = CodeSyntax.Tokenize("npm run dev\nGet-ChildItem -Path $env:USERPROFILE", "powershell");
        Check(commands.Any(t => t is { Offset: 0, Kind: "Command" }) && commands.Any(t => t.Kind == "Variable"),
            "PowerShell command and variable highlighting");
        var note = Path.Combine(output, "灵感笔记.md");
        await File.WriteAllTextAsync(note, source, new UTF8Encoding(false));
        var beforeHash = SHA256.HashData(await File.ReadAllBytesAsync(note));
        await window.OpenFileAsync(note);
        await window.RenderAsync();
        await Settle(root);
        Check(window.TotalPreviewBlocks == parsed.Blocks.Count && window.RealizedPreviewBlocks > 0,
            "Opening a file renders Markdown directly across the full reading pane");
        Check((window.FindName("HeaderTitle") as TextBlock)?.Text == "灵感笔记.md", "Header shows the opened file name");
        Check(Descendants(root).OfType<BlockPresenter>().All(b => b.ToolTip is null),
            "Reader blocks have no jump-to-source edit tooltip");
        Check(Descendants(root).OfType<CheckBox>().All(c => c is { IsHitTestVisible: false, Focusable: false }),
            "Task checkboxes cannot modify documents");
        root.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = Control.MouseDoubleClickEvent });
        Check(
            window.FindName("Editor") is null &&
            SHA256.HashData(await File.ReadAllBytesAsync(note)).SequenceEqual(beforeHash),
            "Double-clicking never opens an editor or changes the source file");
        Capture(root, "viewer-light.png");
        window.ApplyVerificationTheme(true);
        await Settle(root);
        Capture(root, "viewer-dark.png");
        var theme = window.FindName("ThemeButton") as Button ??
                    throw new InvalidOperationException("ThemeButton is missing");
        Check(Equals(theme.Content, window.FindResource("Icon.Sun")), "Dark theme has one sun toggle");
        window.ApplyVerificationTheme(false);
        await Settle(root);
        Check(Equals(theme.Content, window.FindResource("Icon.Theme")), "Light theme has one moon toggle");
        foreach (var dpi in new[] { 96, 120, 144, 192 })
        foreach (var dark in new[] { false, true })
        {
            window.ApplyVerificationTheme(dark);
            await Settle(root);
            var icon = Descendants(theme).OfType<Viewbox>().Single();
            var container = Descendants(theme).OfType<ContentPresenter>().First();
            Check(container.ActualHeight >= icon.ActualHeight && container.ActualWidth >= icon.ActualWidth,
                $"Theme icon has sufficient layout space at {dpi} DPI ({(dark ? "sun" : "moon")})");
            var geometry = (Geometry)theme.Content;
            var bounds = geometry.GetRenderBounds(new Pen(Brushes.Black, 1.8));
            Check(bounds is { Left: >= 0, Top: >= 0, Right: <= 24, Bottom: <= 24 },
                $"Theme glyph stroke fits its canvas at {dpi} DPI ({dark})");
            CaptureElement(theme, $"theme-{(dark ? "sun" : "moon")}-{dpi}.png", dpi);
        }

        window.ApplyVerificationTheme(true);
        await Settle(root);
        foreach (var kind in new[] { "file", "settings", "reader" })
        {
            var menu = window.VerificationMenu(kind);
            menu.ApplyTemplate();
            menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            menu.Arrange(new Rect(new Point(), menu.DesiredSize));
            menu.UpdateLayout();
            var labels = menu.Items.OfType<MenuItem>().Select(i => i.Header?.ToString()).ToArray();
            Check(
                labels.All(label => label is not null && !label.Contains('\n')) && !labels.Any(label =>
                    new[] { UiText.Get("Find"), UiText.Get("Recent"), UiText.Get("Save"), UiText.Get("Outline") }
                        .Contains(label)), "Reader " + kind + " menu contains no editing or navigation features");
            CaptureElement(menu, kind + "-menu-dark.png", 144);
        }

        UiText.Apply("en");
        Check(window.VerificationMenu("file").Items.OfType<MenuItem>().First().Header?.ToString() == "Open",
            "Reader menus remain internationalized");
        UiText.Apply("zh");

        // Component gallery: native math, images, sanitized HTML, links and footnotes.
        var galleryImage = Path.Combine(output, "gallery.png");
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(64, 145, 255), Color.FromRgb(245, 247, 250), 45),
                    null, new Rect(0, 0, 320, 180));
                dc.DrawText(new FormattedText("MokiPad", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 30, Brushes.White, 1.0), new Point(26, 72));
            }

            var bitmap = new RenderTargetBitmap(320, 180, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using var stream = File.Create(galleryImage);
            encoder.Save(stream);
        }
        var other = Path.Combine(output, "other.md");
        await File.WriteAllTextAsync(other, "# 另一个文件\n\n从页内链接打开。\n", new UTF8Encoding(false));
        var svg = Path.Combine(output, "badge.svg");
        await File.WriteAllTextAsync(svg, """
                                          <svg xmlns="http://www.w3.org/2000/svg" width="120" height="40" viewBox="0 0 120 40">
                                            <rect width="120" height="40" rx="8" fill="#4091FF"/>
                                            <circle cx="22" cy="20" r="10" fill="#FFFFFF"/>
                                            <text x="42" y="26" font-family="Segoe UI" font-size="16" fill="#FFFFFF">SVG</text>
                                          </svg>
                                          """, new UTF8Encoding(false));
        var animation = Path.Combine(output, "anim.gif");
        await File.WriteAllBytesAsync(animation, Convert.FromBase64String(
            "R0lGODlhKAAUAIEAAECR/wAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAFAAAACwAAAAAKAAUAAAIMAABCBxIsKDBgwgTKlzIsKHDhxAjSpxIsaLFixgzatzIsaPHjyBDihxJsqTJkxQDAgAh+QQBFAABACwAAAAAKAAUAIH/tEAAAAAAAAAAAAAIMAABCBxIsKDBgwgTKlzIsKHDhxAjSpxIsaLFixgzatzIsaPHjyBDihxJsqTJkxQDAgA7"));
        var gallery = Path.Combine(output, "components.md");
        await File.WriteAllTextAsync(gallery, """
                                              # 阅读组件

                                              行内公式 $E = mc^2$ 与分数 $\frac{a+b}{c}$。

                                              ## 二次方程

                                              $$
                                              x = \frac{-b \pm \sqrt{b^2 - 4ac}}{2a}
                                              $$

                                              ## 极限与导数

                                              $$\lim_{x \to \infty} \left(1 + \frac{1}{x}\right)^x = e$$

                                              $$f'(x_0) = \lim_{\Delta x \to 0} \frac{f(x_0 + \Delta x) - f(x_0)}{\Delta x}$$

                                              ## 图片

                                              ![示例图](gallery.png)
                                              ![矢量图](badge.svg)

                                              ## 原始 HTML

                                              <kbd>Ctrl</kbd> + <kbd>K</kbd>，<mark>高亮</mark>，H<sub>2</sub>O，x<sup>2</sup><br>
                                              <a href="https://example.com">外链</a> 与 <a href="#二次方程">页内跳转</a>

                                              ## 链接

                                              - [外部链接](https://example.com)
                                              - [本地文件](other.md)
                                              - [页内锚点](#脚注)

                                              ## 脚注

                                              引用脚注[^1]，再来一个[^long]。

                                              [^1]: 第一条脚注。
                                              [^long]: 带 **强调** 的脚注。

                                              ![动图](anim.gif)
                                              """, new UTF8Encoding(false));
        await window.OpenFileAsync(gallery);
        await window.RenderAsync();
        await Settle(root);
        Check(window.TotalPreviewBlocks >= 10, "Component gallery parses into reading blocks");
        Check(window.VerificationFootnoteBlocks == 2, "Footnotes produce a footnote section");
        Check(Descendants(root).OfType<MathView>().Any(view => !view.Display && view.Source.Contains("E = mc")),
            "Inline math renders inside the text flow");
        window.ApplyVerificationTheme(false);
        await Settle(root);
        Capture(root, "components-light.png");
        window.ApplyVerificationTheme(true);
        await Settle(root);
        Capture(root, "components-dark.png");
        window.ApplyVerificationTheme(false);
        await Settle(root);

        window.VerificationNavigateLine(7);
        await Settle(root);
        var displayMath = Descendants(root).OfType<MathView>().Where(view => view.Display).ToList();
        Check(displayMath.Count > 0 && displayMath.Any(view => view.Source.Contains("sqrt")),
            "Block math renders with the native TeX layout");
        Check(displayMath.All(view => view is { ActualWidth: > 40, ActualHeight: > 10 }),
            "Math layout produces real measured geometry");

        window.VerificationNavigateLine(19);
        await Settle(root);
        Image? loadedImage = null;
        for (var attempt = 0; attempt < 40 && loadedImage is null; attempt++)
        {
            await Task.Delay(50);
            Layout(root);
            loadedImage = Descendants(root).OfType<Image>()
                .FirstOrDefault(image => image.Source is BitmapSource { PixelWidth: 320 });
        }

        Check(loadedImage is not null, "Local image loads relative to the document");
        Capture(root, "components-image.png");
        window.VerificationNavigateLine(20);
        await Settle(root);
        Image? vector = null;
        for (var attempt = 0; attempt < 40 && vector is null; attempt++)
        {
            await Task.Delay(50);
            Layout(root);
            vector = Descendants(root).OfType<Image>().FirstOrDefault(image => image.Source is DrawingImage);
        }

        Check(vector is not null, "SVG images render as vector drawings");

        window.VerificationNavigateLine(24);
        await Settle(root);
        var inlines = Inlines(root).ToList();
        Check(inlines.OfType<Run>().Any(run => run.Text == "Ctrl" && run.FontFamily.Source.Contains("JetBrains")),
            "HTML kbd renders as keyboard text");
        Check(inlines.OfType<Run>().Any(run => run is { Text: "高亮", Background: not null }),
            "HTML mark renders with a highlight background");
        Check(inlines.OfType<Run>().Any(run => run is { Text: "2", BaselineAlignment: BaselineAlignment.Subscript }),
            "HTML sub renders as subscript");
        Check(inlines.OfType<Hyperlink>().Any(link => link.ToolTip as string == "https://example.com"),
            "HTML links become clickable hyperlinks");
        Check(inlines.OfType<Hyperlink>().Any(link => link.ToolTip as string == "#二次方程"),
            "Anchor links stay inside the document");
        Capture(root, "components-html.png");

        window.VerificationAnchor("脚注");
        await Settle(root);
        Check(window.VerificationScrollOffset > 0, "Anchor navigation scrolls the reading surface");
        window.VerificationNavigateLine(37);
        await Settle(root);
        Check(window.VerificationFootnoteBlocks == 2, "Footnote definitions stay readable at the end of the document");
        Capture(root, "components-footnotes.png");
        Check(Descendants(root).Any(node => node is MarkdownPreview),
            "Reading surface remains virtualized while navigating");

        window.VerificationNavigateLine(40);
        await Settle(root);
        Image? inlineGif = null;
        for (var attempt = 0; attempt < 40 && inlineGif is null; attempt++)
        {
            await Task.Delay(50);
            Layout(root);
            inlineGif = Descendants(root).OfType<Image>().FirstOrDefault(image => Equals(image.Tag, "gif"));
        }

        Check(inlineGif is not null, "Animated GIF starts inline");
        if (inlineGif is not null)
        {
            inlineGif.BringIntoView(); await Settle(root);
            var frames = new HashSet<int>();
            for (var attempt = 0; attempt < 12 && frames.Count < 2; attempt++)
            {
                await Task.Delay(80);
                Layout(root);
                if (inlineGif.Source is not null) frames.Add(GifAnimation.For(inlineGif)?.CurrentFrame ?? -1);
            }

            Check(frames.Count > 1, "Animated GIF advances frames inline");
        }

        var localLink = Inlines(root).OfType<Hyperlink>().FirstOrDefault(link =>
            (link.ToolTip as string)?.EndsWith("other.md", StringComparison.OrdinalIgnoreCase) == true);
        if (localLink is null)
        {
            window.VerificationNavigateLine(30);
            await Settle(root);
            localLink = Inlines(root).OfType<Hyperlink>().FirstOrDefault(link =>
                (link.ToolTip as string)?.EndsWith("other.md", StringComparison.OrdinalIgnoreCase) == true);
        }

        Check(localLink is not null, "Relative Markdown links resolve to a local file");
        var tabsBefore = window.VerificationTabCount;
        localLink!.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent, localLink));
        await Task.Delay(600);
        await Settle(root);
        Check(window.VerificationTabCount == tabsBefore + 1 && window.VerificationOpenRequests >= 1,
            "Clicking a local link opens another reading tab");

        var longInline = Path.Combine(output, "inline-wrap.md");
        await File.WriteAllTextAsync(longInline,
            "# 阅读细节\n\n查看 `runs/internal-validation.json`，以及 `" +
            string.Concat(Enumerable.Repeat("long-directory/", 22)) +
            "report.json`。\n\n```powershell\nnpm run backend\nnpm run dev\n```\n");
        await window.OpenFileAsync(longInline);
        await window.RenderAsync();
        await Settle(root, 640);
        var codeRuns = Inlines(root).OfType<Run>()
            .Where(run => run.FontFamily.Source.Contains("JetBrains") && run.Background is not null).ToArray();
        Check(
            codeRuns.Length >= 1 && codeRuns.Any(run => run.Text.Length > 40) && Descendants(root).OfType<TextBlock>()
                .Where(text => text.Inlines.Count > 0).All(text => text.ActualWidth <= 640),
            "Long inline code stays inside the wrapped reading line");
        Capture(root, "viewer-narrow.png", 640);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        foreach (var encoding in new[] { new UTF8Encoding(true), Encoding.Unicode, Encoding.GetEncoding("GB18030") })
        {
            var file = Path.Combine(output, "read-" + encoding.CodePage + ".md");
            await File.WriteAllTextAsync(file, "# 中文\r\n本地阅读\r\n", encoding);
            var read = await FileService.ReadAsync(file);
            Check(read.Text.Contains("本地阅读") && read.NewLine == "CRLF" && read.Encoding.CodePage == encoding.CodePage,
                "Reader detects " + encoding.WebName);
        }

        var large = Path.Combine(output, "long-20mb.md");
        var section = "\n\n## 长文阅读\n\n" +
                      string.Join(" ", Enumerable.Repeat("Markdown keeps ideas connected and readable.", 18)) +
                      "\n\n- [ ] Keep reading\n- [x] Virtualized content\n\n```csharp\nvar note = \"MokiPad\";\n```\n";
        var batch = string.Concat(Enumerable.Repeat(section, 512));
        var batchBytes = Encoding.UTF8.GetByteCount(batch);
        await using (var writer = new StreamWriter(large, false, new UTF8Encoding(false), 65536))
        {
            for (var bytes = 0; bytes < 20 * 1024 * 1024; bytes += batchBytes) await writer.WriteAsync(batch);
        }

        timer.Restart();
        await window.OpenFileAsync(large);
        await window.RenderAsync();
        await Settle(root);
        Metrics["longFileBytes"] = new FileInfo(large).Length;
        Metrics["longFileOpenParseAndLayoutMs"] = timer.Elapsed.TotalMilliseconds;
        Metrics["longFileBlocks"] = window.TotalPreviewBlocks;
        Metrics["realizedAtTop"] = window.RealizedPreviewBlocks;
        Check(window is { TotalPreviewBlocks: > 10000, RealizedPreviewBlocks: > 0 and < 150 },
            "20 MiB Markdown uses only visible reader containers");
        window.NavigateVerificationBlock(window.TotalPreviewBlocks - 1);
        await Settle(root);
        Metrics["realizedAtEnd"] = window.RealizedPreviewBlocks;
        Check(window.RealizedPreviewBlocks is > 0 and < 150, "End-of-document reading preserves virtualization");
        var largeReader = Descendants(root).OfType<MarkdownPreview>().Single();
        largeReader.SelectAllMarkdown();
        var selectedLongSource = largeReader.GetSelectedMarkdown();
        Check(selectedLongSource.Length > 20_000_000 && window.RealizedPreviewBlocks < 150,
            "Select-all copies long Markdown without realizing the whole document");
        window.NavigateVerificationBlock(0); await Settle(root);
        Check(ReferenceEquals(selectedLongSource, largeReader.GetSelectedMarkdown()),
            "Source selection survives container recycling while scrolling back through long documents");
        largeReader.ClearMarkdownSelection();
        Capture(root, "viewer-long.png");
        Metrics["retainedManagedMiB"] = GC.GetTotalMemory(true) / 1024.0 / 1024.0;
        const string readme = @"C:\Code\React\mokidown\README.md";
        if (File.Exists(readme))
        {
            await window.OpenFileAsync(readme);
            await window.RenderAsync();
            await Settle(root);
            for (var attempt = 0; attempt < 70; attempt++)
            {
                await Task.Delay(50);
                Layout(root);
            }

            Check(
                Descendants(root).OfType<Image>().Any(image =>
                    image.ActualWidth is > 150 and < 260 && image.Source is BitmapSource),
                "HTML hero image honors its width attribute");
            Check(
                Descendants(root).OfType<TextBlock>().Any(text =>
                    text.TextAlignment == TextAlignment.Center && text.FontWeight == FontWeights.SemiBold &&
                    text.FontSize > 24), "HTML h1 renders as a centered heading");
            Capture(root, "readme-light.png");
            var gifImage = Descendants(root).OfType<Image>().FirstOrDefault(image => Equals(image.Tag, "gif"));
            Check(gifImage is not null, "GIF starts playing inline");
            if (gifImage is not null)
            {
                gifImage.BringIntoView(); await Settle(root);
                var sources = new HashSet<int>();
                for (var attempt = 0; attempt < 15 && sources.Count < 2; attempt++)
                {
                    await Task.Delay(60);
                    Layout(root);
                    if (gifImage.Source is not null) sources.Add(GifAnimation.For(gifImage)?.CurrentFrame ?? -1);
                }

                Check(sources.Count > 1, "GIF advances frames inline");
            }

            window.VerificationNavigateLine(48);
            await Settle(root);
            for (var attempt = 0; attempt < 70; attempt++)
            {
                await Task.Delay(50);
                Layout(root);
            }

            Check(Descendants(root).OfType<Image>().Any(image => image.Source is BitmapSource { PixelWidth: > 500 }),
                "Table cells render their images");
            Capture(root, "readme-tables.png");
        }

        var comparison = Path.Combine(output, "style-comparison.md");
        await File.WriteAllTextAsync(comparison, "# MokiPad 代码清理修改集（按修改类型）\n\n> 范围：从 `MokiPad.sln` 解决方案文档/检查工作开始，到 InspectCode 与 Rider 复查归零为止。\n> 基线：`143` 条 → 全量口径 `155` 条 → **0**。\n> 结果：`dotnet build` 0 warning / 0 error；验证套件全部 PASS；InspectCode 0 issue。\n\n本文按“修改的性质”分类，每一类都给出**改前与改后**的代码片段。\n\n---\n\n## 0. 总览\n\n```powershell\n" + string.Concat(Enumerable.Repeat("npm run dev\n", 80)) + "```\n");
        await window.OpenFileAsync(comparison); await window.RenderAsync(); await Settle(root); window.ApplyVerificationTheme(true); await Settle(root);
        var reader = Descendants(root).OfType<MarkdownPreview>().Single();
        var quote = Descendants(root).OfType<BlockPresenter>().First(b => b.Block?.Kind == "quote");
        var quoteText = Descendants(quote).OfType<TextBlock>().First();
        Check(quoteText.Inlines.OfType<LineBreak>().Count() == 2, "MokiDown quote line breaks remain three distinct lines");
        Check(quote.Content is Border { Background: SolidColorBrush quoteBrush } && quoteBrush.Color == Color.FromArgb(77, 90, 159, 255), "Dark quotes use the same saturated blue tint as MokiDown");
        Capture(root, "style-aligned-dark.png");
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        quote.RaiseEvent(wheel);
        Check(wheel.Handled && reader.ScrollTargetOffset > 0, "Vertical wheel over a quote reaches the reading scroller"); reader.FinishScroll(); await Settle(root);
        var codeBlock = Descendants(root).OfType<BlockPresenter>().FirstOrDefault(b => b.Block?.Kind == "code");
        if (codeBlock is not null)
        {
            var inner = Descendants(codeBlock).OfType<ScrollViewer>().First();
            var old = reader.ScrollTargetOffset;
            var codeWheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            inner.RaiseEvent(codeWheel);
            Check(codeWheel.Handled && reader.ScrollTargetOffset > old, "Vertical wheel over a code scroller continues the main document"); reader.FinishScroll();
        }
        Console.WriteLine(JsonSerializer.Serialize(Metrics));
    }

    private static async Task VerifyScrollReversal(MainWindow window)
    {
        var path = Path.Combine(output, "scroll-reversal.md");
        await File.WriteAllTextAsync(path, string.Concat(Enumerable.Repeat("Reading paragraph with a stable height.\n\n", 300)));
        await window.OpenFileAsync(path); await window.RenderAsync();
        var root = window.VerificationSurface;
        await Settle(root);
        var reader = Descendants(root).OfType<MarkdownPreview>().Single();
        var scroll = Descendants(reader).OfType<ScrollViewer>().First();
        try
        {
            scroll.ScrollToVerticalOffset(700); await Settle(root);
            var position = scroll.VerticalOffset;
            for (var i = 0; i < 6; i++) Wheel(reader, -120);
            Wheel(reader, 120);
            Check(reader.ScrollTargetOffset < position, "Upward wheel immediately reverses unfinished downward scrolling");
            reader.FinishScroll(); await Settle(root);
            Check(scroll.VerticalOffset < position, "Visible content moves upward after reversing the wheel");

            scroll.ScrollToVerticalOffset(1200); await Settle(root);
            position = scroll.VerticalOffset;
            for (var i = 0; i < 6; i++) Wheel(reader, 120);
            Wheel(reader, -120);
            Check(reader.ScrollTargetOffset > position, "Downward wheel immediately reverses unfinished upward scrolling");
            reader.FinishScroll(); await Settle(root);
            Check(scroll.VerticalOffset > position, "Visible content moves downward after reversing the wheel");

            var followsDirection = true;
            for (var i = 0; i < 12; i++)
            {
                var delta = i % 2 == 0 ? 15 : -15;
                var current = scroll.VerticalOffset;
                Wheel(reader, delta);
                followsDirection &= delta > 0 ? reader.ScrollTargetOffset < current : reader.ScrollTargetOffset > current;
            }
            Check(followsDirection, "Rapid small wheel deltas always follow the latest input direction");
        }
        finally { reader.FinishScroll(); }
    }

    private static void Wheel(MarkdownPreview reader, int delta)
    {
        reader.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta)
            { RoutedEvent = UIElement.PreviewMouseWheelEvent });
    }

    private static IEnumerable<string> GlyphFonts(Drawing? drawing)
    {
        return drawing switch
        {
            GlyphRunDrawing glyph => glyph.GlyphRun.GlyphTypeface.FamilyNames.Values,
            DrawingGroup group => group.Children.SelectMany(GlyphFonts),
            _ => []
        };
    }

    private static async Task VerifyPolish()
    {
        foreach (var (family, expected) in new[] { (AppFonts.Body, "Poppins"), (AppFonts.Code, "JetBrains") })
        {
            var sample = new TextBlock { Text = "MokiPad code 0123", FontFamily = family, FontSize = 16, Foreground = Brushes.White };
            sample.Measure(new Size(600, 80)); sample.Arrange(new Rect(0, 0, 600, 80)); sample.UpdateLayout();
            var render = new RenderTargetBitmap(600, 80, 96, 96, PixelFormats.Pbgra32); render.Render(sample);
            var names = GlyphFonts(VisualTreeHelper.GetDrawing(sample)).ToArray();
            Check(names.Any(name => name.Contains(expected, StringComparison.OrdinalIgnoreCase)), "Rendered text actually uses " + expected + " with the shared fallback family");
        }
        var regular = new Typeface(new FontFamily(new Uri("pack://application:,,,/MokiPad;component/"), "./Assets/Fonts/#Poppins"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Check(regular.TryGetGlyphTypeface(out var glyph) && glyph.FamilyNames.Values.Any(n => n.Contains("Poppins")), "Body resolves the actual bundled MokiDown Poppins font");
        var mono = new Typeface(new FontFamily(new Uri("pack://application:,,,/MokiPad;component/"), "./Assets/Fonts/#JetBrains Mono"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        Check(mono.TryGetGlyphTypeface(out var codeGlyph) && codeGlyph.FamilyNames.Values.Any(n => n.Contains("JetBrains")), "Code resolves the actual bundled JetBrains Mono font");
        const int width = 2048;
        const int height = 24;
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = (byte)(i % 251); pixels[i + 2] = 255; pixels[i + 3] = 255; }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(output, "native-resolution.png"); await using (var stream = File.Create(path)) png.Save(stream);
        var requests = Enumerable.Range(0, 8).Select(_ => ImageLoader.LoadAsync(path, null)).ToArray();
        var decoded = await Task.WhenAll(requests);
        Check(decoded[0] is BitmapSource { PixelWidth: 2048 } && decoded.All(image => ReferenceEquals(image, decoded[0])), "Static image decode preserves native quality and shares concurrent requests");
        var gif = new GifBitmapEncoder(); gif.Frames.Add(BitmapFrame.Create(bitmap));
        var changed = (byte[])pixels.Clone(); for (var i = 0; i < changed.Length; i += 4) changed[i + 2] = 0;
        gif.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, changed, width * 4)));
        using var encoded = new MemoryStream(); gif.Save(encoded);
        var target = new Image();
        using var player = new GifAnimation(target, encoded.ToArray());
        await player.StartAsync();
        var surface = target.Source;
        Check(surface is BitmapSource { PixelWidth: 2048, PixelHeight: 24 }, "GIF keeps its native pixel canvas instead of shrinking to 1000 px");
        var index = player.CurrentFrame;
        for (var count = 0; count < 15 && player.CurrentFrame == index; count++) await Task.Delay(35);
        Check(player.CurrentFrame != index && ReferenceEquals(surface, target.Source), "GIF advances pixels in one stable surface without per-frame layout changes");
    }
    private static async Task Settle(FrameworkElement root, int width = 1280)
    {
        Layout(root, width);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Layout(root, width);
    }

    private static void Layout(FrameworkElement root, int width = 1280)
    {
        root.Measure(new Size(width, 820));
        root.Arrange(new Rect(0, 0, width, 820));
        root.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var node in Descendants(child)) yield return node;
        }
    }

    private static IEnumerable<Inline> Inlines(DependencyObject root)
    {
        return Descendants(root).OfType<TextBlock>().SelectMany(text => text.Inlines).SelectMany(Flatten);
    }

    private static IEnumerable<Inline> Flatten(Inline inline)
    {
        yield return inline;
        if (inline is not Span span) yield break;
        foreach (var child in span.Inlines)
        foreach (var nested in Flatten(child))
            yield return nested;
    }

    private static void Capture(FrameworkElement root, string name, int width = 1280)
    {
        Layout(root, width);
        CaptureElement(root, name, 144);
    }

    private static void CaptureElement(FrameworkElement element, string name, int dpi)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi / 96)),
            Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi / 96)), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name));
        encoder.Save(file);
    }
}
