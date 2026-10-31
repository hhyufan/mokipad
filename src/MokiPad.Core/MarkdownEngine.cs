using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MokiPad.Core;

public sealed record PreviewBlock(
    string Kind,
    int Line,
    int Offset,
    string Text = "",
    ContainerInline? Inline = null,
    int Level = 0,
    string Prefix = "",
    bool? Checked = null,
    string Language = "",
    string[][]? Rows = null,
    string? CodeSource = null,
    string Anchor = "",
    int FootnoteOrder = -1,
    ContainerInline?[][]? Cells = null,
    string[]? ColumnAlign = null,
    int CopyStart = -1,
    int CopyEnd = -1,
    bool WholeSource = false);

public sealed record MarkdownResult(IReadOnlyList<PreviewBlock> Blocks, double ParseMs, string Source = "");

/// <summary>Background-only parsing. Models contain no DispatcherObjects; WPF visuals are made on demand.</summary>
public static class MarkdownEngine
{
    // GFM tables/tasks/autolinks, emphasis extras (~~ == ++ ^^), $math$ and [^notes].
    private static readonly Regex DashPattern = new("-+", RegexOptions.Compiled);
    private static readonly Regex WhitespacePattern = new("\\s+", RegexOptions.Compiled);

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseTaskLists().UseAutoLinks().UseEmphasisExtras()
        .UseMathematics().UseFootnotes().UsePreciseSourceLocation().Build();

    public static MarkdownResult Parse(string source, CancellationToken cancellation = default)
    {
        var watch = Stopwatch.StartNew();
        var doc = Markdown.Parse(source, Pipeline);
        var blocks = new List<PreviewBlock>();
        var anchors = new Dictionary<string, int>(StringComparer.Ordinal);





        foreach (var block in doc) Add(block);
        return new MarkdownResult(blocks, watch.Elapsed.TotalMilliseconds, source);
        string UniqueAnchor(string title)
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

        void Add(Block block, int depth = 0, string prefix = "")
        {
            cancellation.ThrowIfCancellationRequested();
            var firstBlock = blocks.Count;
            switch (block)
            {
                case HeadingBlock h:
                    var title = Plain(h.Inline);
                    blocks.Add(new PreviewBlock("heading", h.Line + 1, h.Span.Start, title, h.Inline, h.Level,
                        Anchor: UniqueAnchor(title)));
                    break;
                case MathBlock math:
                    var tex = math.Lines.ToString().Trim();
                    if (tex.Length > 0) blocks.Add(new PreviewBlock("math", math.Line + 1, math.Span.Start, tex));
                    break;
                case HtmlBlock html:
                    var raw = html.Lines.ToString();
                    if (!string.IsNullOrWhiteSpace(raw))
                        blocks.Add(new PreviewBlock("html", html.Line + 1, html.Span.Start, raw));
                    break;
                case FootnoteGroup group:
                    AddFootnotes(group);
                    break;
                case FencedCodeBlock code:
                    AddChunks(code.Lines.ToString(), "code", code.Line + 1, code.Span.Start, code.Info ?? "");
                    break;
                case CodeBlock code:
                    AddChunks(code.Lines.ToString(), "code", code.Line + 1, code.Span.Start);
                    break;
                case Table table:
                    var tableRows = table.OfType<TableRow>().ToList();
                    var rows = tableRows.Select(row => row.OfType<TableCell>()
                        .Select(cell => string.Join(" ", cell.OfType<ParagraphBlock>().Select(p => Plain(p.Inline))))
                        .ToArray()).ToArray();
                    var cells = tableRows.Select(row => row.OfType<TableCell>()
                        .Select(cell => cell.OfType<ParagraphBlock>().FirstOrDefault()?.Inline).ToArray()).ToArray();
                    var align = table.ColumnDefinitions.Select(column => column.Alignment switch
                    {
                        TableColumnAlign.Center => "center",
                        TableColumnAlign.Right => "right",
                        _ => "left"
                    }).ToArray();
                    // A very large table must not become one unbounded WPF Grid.
                    for (var index = 1; index < rows.Length; index += 32)
                        blocks.Add(new PreviewBlock("table", table.Line + 1, table.Span.Start,
                            Rows: [rows[0], .. rows.Skip(index).Take(32)],
                            Cells: [cells[0], .. cells.Skip(index).Take(32)],
                            ColumnAlign: align));
                    if (rows.Length == 1)
                        blocks.Add(new PreviewBlock("table", table.Line + 1, table.Span.Start, Rows: rows, Cells: cells,
                            ColumnAlign: align));
                    break;
                case ListBlock list:
                    var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
                    foreach (var item in list.OfType<ListItemBlock>())
                    {
                        var first = true;
                        foreach (var child in item)
                        {
                            Add(child, depth + 1, first ? list.IsOrdered ? $"{number}." : "•" : "");
                            first = false;
                        }

                        number++;
                    }

                    break;
                case QuoteBlock quote:
                    foreach (var child in quote)
                    {
                        var before = blocks.Count;
                        Add(child, depth, prefix);
                        for (var i = before; i < blocks.Count; i++)
                            if (blocks[i].Kind == "paragraph")
                                blocks[i] = blocks[i] with { Kind = "quote" };
                    }

                    break;
                case ParagraphBlock p:
                    // A paragraph that only holds one $$...$$ formula reads as display math.
                    if (p.Inline is { FirstChild: MathInline { NextSibling: null, DelimiterCount: >= 2 } displayMath })
                    {
                        var displayTex = displayMath.Content.ToString().Trim();
                        if (displayTex.Length > 0)
                            blocks.Add(new PreviewBlock("math", p.Line + 1, p.Span.Start, displayTex));
                        break;
                    }

                    var plain = Plain(p.Inline);
                    var task = p.Inline?.Descendants<TaskList>().FirstOrDefault();
                    if (plain.Length > 8192) AddChunks(plain, "paragraph", p.Line + 1, p.Span.Start);
                    else
                        blocks.Add(new PreviewBlock("paragraph", p.Line + 1, p.Span.Start, "", p.Inline, depth,
                            prefix, task?.Checked));
                    break;
                case ThematicBreakBlock:
                    blocks.Add(new PreviewBlock("rule", block.Line + 1, block.Span.Start));
                    break;
                case ContainerBlock container:
                    foreach (var child in container) Add(child, depth, prefix);
                    break;
                case LeafBlock leaf:
                    AddChunks(leaf.Lines.ToString(), "paragraph", leaf.Line + 1, leaf.Span.Start);
                    break;
            }
            // Chunks share the complete original syntax node. Never synthesize a
            // fence/table from rendered text, and never duplicate it when copying.
            for (var i = firstBlock; i < blocks.Count; i++)
                if (blocks[i].CopyEnd < 0) blocks[i] = WithSource(blocks[i], block);
        }

        PreviewBlock WithSource(PreviewBlock preview, Block syntax)
        {
            var start = Math.Clamp(syntax.Span.Start, 0, source.Length);
            var end = Math.Clamp(syntax.Span.End + 1, start, source.Length);
            var lineStart = start == 0 ? 0 : source.LastIndexOf('\n', start - 1) + 1;
            return preview with { CopyStart = lineStart, CopyEnd = end };
        }

        void AddFootnotes(FootnoteGroup group)
        {
            var used = group.OfType<Footnote>().Where(f => f.Order >= 0).OrderBy(f => f.Order).ToList();
            if (used.Count == 0) return;
            blocks.Add(new PreviewBlock("rule", group.Line + 1, group.Span.Start, CopyEnd: 0));
            foreach (var footnote in used)
            {
                var paragraphs = footnote.OfType<ParagraphBlock>().ToList();
                if (paragraphs.Count == 0)
                {
                    var plain = string.Join(" ",
                        footnote.OfType<LeafBlock>().Select(leaf => leaf.Lines.ToString().Trim()));
                    blocks.Add(WithSource(new PreviewBlock("footnote", footnote.Line + 1, footnote.Span.Start, plain,
                        FootnoteOrder: footnote.Order), footnote));
                    continue;
                }

                var first = true;
                foreach (var paragraph in paragraphs)
                {
                    blocks.Add(WithSource(new PreviewBlock(first ? "footnote" : "footnote-cont", paragraph.Line + 1,
                        paragraph.Span.Start,
                        "", paragraph.Inline, FootnoteOrder: footnote.Order), footnote));
                    first = false;
                }
            }
        }

        void AddChunks(string text, string kind, int line, int offset, string language = "")
        {
            if (text.Length == 0 && kind == "code")
                blocks.Add(new PreviewBlock("code", line, offset, Language: language));
            // Bound work per realized item, even for a single huge code fence/paragraph.
            for (var cursor = 0; cursor < text.Length;)
            {
                cancellation.ThrowIfCancellationRequested();
                var end = Math.Min(cursor + 4096, text.Length);
                var newLine = end < text.Length ? text.LastIndexOf('\n', end - 1, end - cursor) : -1;
                if (newLine > cursor + 1024) end = newLine + 1;
                if (end < text.Length && char.IsHighSurrogate(text[end - 1])) end--;
                var part = text[cursor..end];
                blocks.Add(new PreviewBlock(kind, line, offset + cursor, part, Language: language,
                    CodeSource: kind == "code" ? text : null, WholeSource: kind == "paragraph"));
                line += part.Count(c => c == '\n');
                cursor = end;
            }
        }

    }

    /// <summary>Heading anchor compatible with MokiDown's preview slugs.</summary>
    public static string Slugify(string text)
    {
        var builder = new StringBuilder(string.Concat(text.ToLowerInvariant()
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or ' ')));
        return DashPattern.Replace(WhitespacePattern.Replace(builder.ToString(), "-"), "-").Trim();
    }


    public static string Plain(ContainerInline? inline)
    {
        if (inline is null) return "";
        var text = new StringBuilder();


        Walk(inline);
        return text.ToString();
        void Walk(Inline node)
        {
            switch (node)
            {
                case LiteralInline literal: text.Append(literal.Content); break;
                case CodeInline code: text.Append(code.Content); break;
                case LineBreakInline: text.Append(' '); break;
                case MathInline math: text.Append(math.Content); break;
                case AutolinkInline link: text.Append(link.Url); break;
                case ContainerInline container:
                    for (var child = container.FirstChild; child is not null; child = child.NextSibling) Walk(child);
                    break;
            }
        }

    }

}
