using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Markdig.Syntax.Inlines;
using MokiPad.Controls;
using MokiPad.Core;
using MokiPad.Services;
using Inline = System.Windows.Documents.Inline;

namespace MokiPad.Verification;

internal static class MarkdownCopyVerification
{
    internal static async Task Run(MainWindow window, string output, Action<bool, string> check, Action<FrameworkElement, string> capture)
    {
        void Equal(string expected, string actual, string name)
        {
            if (expected != actual) throw new InvalidOperationException(name + "\nEXPECTED: " + expected + "\nACTUAL: " + actual);
            check(true, name);
        }
        void Block(string source, string kind, string name)
        {
            var parsed = MarkdownEngine.Parse(source);
            var block = parsed.Blocks.First(b => b.Kind == kind);
            Equal(source.TrimEnd('\r', '\n'), MarkdownCopy.Block(source, block).TrimEnd('\r', '\n'), name);
        }
        Block("````csharp {.demo}\r\nConsole.WriteLine(\"```\");\r\n````\r\n", "code", "Code copy preserves original long fences, info attributes and CRLF");
        Block("~~~python\nprint('original')\n~~~", "code", "Tilde code fences remain tilde fences");
        Block("    first indented line\n    second line", "code", "Indented code preserves the original Markdown indentation");
        Block("| A | B |\n| :--- | ---: |\n| a\\|b | ![图](./图.png \"title\") |", "table", "Table copy preserves alignment, escaped pipes and embedded image syntax");
        Block("![星光](<./images/my image.png> \"title\")", "paragraph", "Markdown images copy their URL and original syntax instead of alt text");
        Block("<figure>\n<img src=\"image.png\" width=\"300\" alt=\"图\">\n<figcaption>caption</figcaption>\n</figure>", "html", "HTML blocks retain tags and attributes");
        Block("$$\n\\frac{a}{b} + \\sqrt{x}\n$$", "math", "Display equations retain delimiters and TeX source");
        Block("A setext heading\n================", "heading", "Setext heading copy retains original heading syntax");
        var nested = "> - [x] preserved **task**";
        Block(nested, "quote", "Quote and task prefixes remain in copied source");

        void Partial(string source, string selected, string expected, string name)
        {
            var parsed = MarkdownEngine.Parse(source); var start = source.IndexOf(selected, StringComparison.Ordinal);
            Equal(expected, MarkdownCopy.Selection(source, parsed.Blocks, new(0, start), new(0, start + selected.Length)), name);
        }
        Partial("hello **world** tail", "orl", "**orl**", "Partial bold selection receives balanced Markdown markers");
        Partial("hello <kbd>Control</kbd> tail", "ontro", "<kbd>ontro</kbd>", "Partial inline HTML selection receives balanced tags");
        Partial("## heading title", "title", "## title", "Partial heading selection preserves heading semantics");
        Partial("- [x] finished task", "task", "- [x] task", "Partial task selection retains the original checkbox marker");
        Partial("plain words here", "words", "words", "Ordinary text selection copies only the requested words");
        Partial("hello **<kbd>Control</kbd>** tail", "ontro", "**<kbd>ontro</kbd>**", "Nested Markdown and HTML wrappers close in the proper order");

        var codeSource = "```js\n" + string.Concat(Enumerable.Repeat("console.log('chunk');\n", 800)) + "```";
        var codeParsed = MarkdownEngine.Parse(codeSource);
        check(codeParsed.Blocks.Count > 1, "Large-code copy test actually uses virtualized chunks");
        Equal(codeSource, MarkdownCopy.Selection(codeSource, codeParsed.Blocks, new(0, 0), new(codeParsed.Blocks.Count - 1, codeSource.Length)), "Cross-chunk code selection emits one complete fence without duplication");
        var tableSource = "| Name | Value |\n| --- | ---: |\n" + string.Concat(Enumerable.Range(0, 100).Select(i => $"| row{i} | {i} |\n"));
        var tableParsed = MarkdownEngine.Parse(tableSource);
        check(tableParsed.Blocks.Count > 1, "Large-table copy test actually uses several table chunks");
        Equal(tableSource.TrimEnd('\n'), MarkdownCopy.Selection(tableSource, tableParsed.Blocks, new(0, 0), new(tableParsed.Blocks.Count - 1, tableSource.Length)).TrimEnd('\n'), "Cross-chunk table selection copies one original Markdown table");
        var referenceSource = "![preview][picture]\n\n[picture]: ./asset.png \"original title\"";
        var reference = MarkdownEngine.Parse(referenceSource);
        Equal(referenceSource, MarkdownCopy.Selection(referenceSource, reference.Blocks, new(0, 0), new(0, reference.Blocks[0].CopyEnd)), "Reference image selection includes its original link definition");
        Equal(referenceSource, MarkdownCopy.Block(referenceSource, reference.Blocks[0]), "Context-menu image copy also includes the reference definition");
        var referenceTable = "| 图 |\n| --- |\n| ![x][asset] |\n\n[asset]: ./table.png";
        var referenceTableBlocks = MarkdownEngine.Parse(referenceTable).Blocks;
        Equal(referenceTable, MarkdownCopy.Block(referenceTable, referenceTableBlocks.First(b => b.Kind == "table")), "Table copy also includes reference definitions from its cells");
        var footnoteSource = "text[^note]\n\n[^note]: Footnote definition";
        var footnotes = MarkdownEngine.Parse(footnoteSource);
        Equal(footnoteSource, MarkdownCopy.Selection(footnoteSource, footnotes.Blocks, new(0, 0), new(0, footnotes.Blocks[0].CopyEnd)), "Footnote references retain the definition needed by Markdown");
        check(MarkdownCopy.SourceOffset("a\\*b &amp; c", new(0, 13), 3) == 4, "Glyph-to-source mapping preserves backslash escapes");
        var payload = MarkdownClipboard.Payload("![x](a.png)");
        Equal("![x](a.png)", (string)payload.GetData(DataFormats.UnicodeText, false)!, "Clipboard plain-text payload is Markdown source");
        check(payload.GetDataPresent("text/markdown", false) && !payload.GetDataPresent(DataFormats.Html, false) && !payload.GetDataPresent(DataFormats.Bitmap, false), "Clipboard advertises Markdown without substituting rendered HTML or bitmaps");

        var pixels = new byte[240 * 54 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 255; pixels[i + 1] = (byte)(170 + i / 4 % 65); pixels[i + 2] = 90; pixels[i + 3] = 255; }
        var bitmap = BitmapSource.Create(240, 54, 96, 96, PixelFormats.Bgra32, null, pixels, 240 * 4);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(output, "selection-image.png"))) png.Save(stream);
        const string sample = "# 选择复制 Markdown\n\n普通文字与 **强调内容**，行内 `code()`，以及 $a^2+b^2=c^2$。\n\n![星光图](selection-image.png \"demo\")\n\n<img src=\"selection-image.png\" width=\"160\" alt=\"HTML image\">\n\n```csharp\nConsole.WriteLine(\"Markdown\");\n```\n\n| 名称 | 内容 |\n| :--- | ---: |\n| 图片 | ![x](selection-image.png) |\n\n$$\n\\frac{a}{b}\n$$\n";
        var path = Path.Combine(output, "selection.md"); await File.WriteAllTextAsync(path, sample);
        await window.OpenFileAsync(path); await window.RenderAsync();
        var root = window.VerificationSurface; await Settle(root);
        await Task.Delay(60); await Settle(root);
        var reader = Descendants(root).OfType<MarkdownPreview>().Single();
        var parsedSample = MarkdownEngine.Parse(sample);
        var boldStart = sample.IndexOf("强调内容", StringComparison.Ordinal);
        reader.SelectRange(new(1, boldStart), new(1, boldStart + 4));
        Equal("**强调内容**", reader.GetSelectedMarkdown(), "Rendered selection routes through original Markdown rather than TextBlock.Text");
        capture(root, "selection-text.png");
        var imageBlock = parsedSample.Blocks.ToList().FindIndex(b => b.Inline?.FirstChild is LinkInline { IsImage: true });
        reader.SelectRange(new(imageBlock, parsedSample.Blocks[imageBlock].CopyStart), new(imageBlock, parsedSample.Blocks[imageBlock].CopyEnd));
        Equal("![星光图](selection-image.png \"demo\")", reader.GetSelectedMarkdown(), "Rendered image selection copies a Markdown link");
        var imageView = Descendants(reader).OfType<Image>().First(i => i.Source is BitmapSource && i.ActualWidth > 10);
        reader.SelectAt(imageView.TransformToAncestor(reader).Transform(new Point(imageView.ActualWidth / 2, imageView.ActualHeight / 2)));
        Equal("![星光图](selection-image.png \"demo\")", reader.GetSelectedMarkdown(), "Hit-testing a loaded image selects its source instead of adjacent text");
        var formulaView = Descendants(reader).OfType<MokiPad.Rendering.MathView>().First();
        reader.SelectAt(formulaView.TransformToAncestor(reader).Transform(new Point(formulaView.ActualWidth / 2, formulaView.ActualHeight / 2)));
        Equal("$a^2+b^2=c^2$", reader.GetSelectedMarkdown(), "Hit-testing inline math selects original delimiters and TeX");
        var ordinaryRun = Inlines(reader).OfType<Run>().First(r => r.Text.StartsWith("普通文字", StringComparison.Ordinal));
        var textBlock = ordinaryRun.Parent;
        while (textBlock is FrameworkContentElement content) textBlock = content.Parent;
        var rectangle = ordinaryRun.ContentStart.GetPositionAtOffset(2)!.GetCharacterRect(LogicalDirection.Forward);
        var hit = reader.HitPosition(((TextBlock)textBlock).TransformToAncestor(reader).Transform(new Point(rectangle.Left + .1, rectangle.Top + rectangle.Height / 2)));
        check(hit?.Offset == sample.IndexOf("普通文字", StringComparison.Ordinal) + 2, "Mouse text positions map accurately to source offsets");
        Point Caret(Run run, int offset)
        {
            DependencyObject owner = run.Parent;
            while (owner is FrameworkContentElement content) owner = content.Parent;
            var box = run.ContentStart.GetPositionAtOffset(offset)!.GetCharacterRect(LogicalDirection.Forward);
            return ((TextBlock)owner).TransformToAncestor(reader).Transform(new Point(box.Left + .1, box.Top + box.Height / 2));
        }
        var boldRun = Inlines(reader).OfType<Run>().First(r => r.Text == "强调内容");
        reader.BeginSelection(Caret(ordinaryRun, 2)); reader.DragSelectionTo(Caret(boldRun, 3)); reader.EndDrag();
        Equal("文字与 **强调内**", reader.GetSelectedMarkdown(), "Point-driven drag selection crosses a formatting boundary with balanced source");
        var codeView = Descendants(reader).OfType<BlockPresenter>().First(p => p.Block?.Kind == "code");
        var codePoint = codeView.TransformToAncestor(reader).Transform(new Point(codeView.ActualWidth / 2, codeView.ActualHeight / 2));
        reader.BeginSelection(codePoint, ModifierKeys.Shift); reader.EndDrag();
        check(reader.GetSelectedMarkdown().StartsWith("文字与 **强调内容**", StringComparison.Ordinal) && reader.GetSelectedMarkdown().EndsWith("```", StringComparison.Ordinal), "Shift-click extends the existing anchor through an entire fenced code block");
        reader.BeginSelection(codePoint); reader.DragSelectionTo(Caret(ordinaryRun, 2)); reader.EndDrag();
        check(reader.GetSelectedMarkdown().StartsWith("文字与", StringComparison.Ordinal) && reader.GetSelectedMarkdown().EndsWith("```", StringComparison.Ordinal), "Backward drag includes atomic structures and the partial starting paragraph");
        reader.SelectRange(new(0, 0), new(parsedSample.Blocks.Count - 1, sample.Length));
        Equal(sample.TrimEnd('\n'), reader.GetSelectedMarkdown().TrimEnd('\n'), "Cross-block selection retains headings, HTML, code, table and formulas in order");
        capture(root, "selection-structures.png");
        reader.SelectAllMarkdown();
        check(ReferenceEquals(sample, reader.GetSelectedMarkdown()) || reader.GetSelectedMarkdown() == sample, "Select-all copies the original document with all source syntax");
        reader.ClearMarkdownSelection();
        ApplicationCommands.SelectAll.Execute(null, window);
        Equal(sample, reader.GetSelectedMarkdown(), "Routed Select All command reaches the document from window focus");
        check(ApplicationCommands.Copy.CanExecute(null, window) && window.InputBindings.OfType<KeyBinding>().Any(b => b.Key == Key.C && b.Modifiers == ModifierKeys.Control), "Ctrl+C is explicitly bound and enabled for a Markdown selection");
        var menu = window.VerificationMenu("reader");
        check(menu.Items.OfType<MenuItem>().Any(i => Equals(i.Header, UiText.Get("CopySelectedMarkdown"))) && menu.Items.OfType<MenuItem>().Any(i => Equals(i.Header, UiText.Get("CopyBlockMarkdown"))), "Selection and block-copy menu actions are localized");
        Equal(sample, await File.ReadAllTextAsync(path), "Selection and source-copy preparation never modify the document");
        reader.SelectRange(default, default);
        check(!ApplicationCommands.Copy.CanExecute(null, window), "Copy command is disabled when the selection is cleared");
        var wrap = "# Wrapped selection\n\n" + string.Concat(Enumerable.Repeat("wrapped paragraph content with ordinary words ", 35));
        var wrapPath = Path.Combine(output, "selection-wrap.md"); await File.WriteAllTextAsync(wrapPath, wrap);
        await window.OpenFileAsync(wrapPath); await window.RenderAsync(); await Settle(root);
        reader = Descendants(root).OfType<MarkdownPreview>().Single();
        var wrapStart = wrap.IndexOf("wrapped paragraph", StringComparison.Ordinal) + 10;
        reader.SelectRange(new(1, wrapStart), new(1, wrapStart + 350));
        Equal(wrap.Substring(wrapStart, 350), reader.GetSelectedMarkdown(), "Wrapped multi-line text preserves a precise partial source range");
        capture(root, "selection-wrapped.png");
        var plain = "A plain file with literal `code` and **stars**.\r\nSecond line.";
        var plainPath = Path.Combine(output, "selection.txt"); await File.WriteAllTextAsync(plainPath, plain);
        await window.OpenFileAsync(plainPath); await window.RenderAsync(); await Settle(root);
        reader = Descendants(root).OfType<MarkdownPreview>().Single();
        var plainStart = plain.IndexOf("literal", StringComparison.Ordinal);
        reader.SelectRange(new(0, plainStart), new(0, plainStart + 14));
        Equal("literal `code`", reader.GetSelectedMarkdown(), "Plain-text files also support precise partial selection");
        reader.ClearMarkdownSelection();
    }

    private static async Task Settle(FrameworkElement root)
    {
        root.Measure(new Size(1280, 820)); root.Arrange(new Rect(0, 0, 1280, 820)); root.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); root.UpdateLayout();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static IEnumerable<Inline> Inlines(DependencyObject root)
    {
        static IEnumerable<Inline> Walk(InlineCollection nodes)
        {
            foreach (var node in nodes) { yield return node; if (node is Span span) foreach (var child in Walk(span.Inlines)) yield return child; }
        }
        return Descendants(root).OfType<TextBlock>().SelectMany(t => Walk(t.Inlines));
    }
}
