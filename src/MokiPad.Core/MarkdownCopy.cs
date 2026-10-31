using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig.Extensions.Footnotes;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MokiPad.Core;

public readonly record struct SourceRange(int Start, int End)
{
    public bool Valid => Start >= 0 && End > Start;
    public bool Intersects(SourceRange other) => Start < other.End && End > other.Start;
}

public readonly record struct ReadingPosition(int Block, int Offset);

/// <summary>Copies original source slices, with balanced wrappers at partial selection boundaries.</summary>
public static class MarkdownCopy
{
    private static readonly Regex HtmlTag = new(@"^<\s*(/?)\s*([a-zA-Z][\w:-]*)\b", RegexOptions.Compiled);
    private static readonly HashSet<string> VoidTags = new(StringComparer.OrdinalIgnoreCase)
        { "img", "br", "hr", "input", "wbr", "source", "area", "base", "embed", "link", "meta", "param", "track", "col" };

    public static bool IsAtomic(PreviewBlock block) => block.Kind is "code" or "table" or "math" or "html" or "rule" or "footnote" or "footnote-cont" || block.WholeSource;
    public static SourceRange Range(PreviewBlock block) => new(block.CopyStart, block.CopyEnd);

    public static string Block(string source, PreviewBlock block) => Range(block) is { Valid: true } range && range.End <= source.Length
        ? Selection(source, [block], new(0, range.Start), new(0, range.End)) : block.Kind == "code" ? Fence(block.CodeSource ?? block.Text, block.Language) : block.Text;

    public static string Selection(string source, IReadOnlyList<PreviewBlock> blocks, ReadingPosition anchor, ReadingPosition focus, bool all = false)
    {
        if (all) return source;
        var (start, end) = Compare(anchor, focus) <= 0 ? (anchor, focus) : (focus, anchor);
        if (start == end || blocks.Count == 0) return "";
        var pieces = new List<(PreviewBlock Block, SourceRange Range)>();
        for (var index = Math.Max(0, start.Block); index <= Math.Min(end.Block, blocks.Count - 1); index++)
        {
            var block = blocks[index]; var range = Range(block);
            if (!range.Valid || range.End > source.Length) continue;
            if (!IsAtomic(block)) range = new(Math.Max(range.Start, index == start.Block ? start.Offset : range.Start),
                Math.Min(range.End, index == end.Block ? end.Offset : range.End));
            if (!range.Valid) continue;
            // A long code fence or table has several virtualized visual chunks.
            if (pieces.Count > 0 && pieces[^1].Range == range) continue;
            pieces.Add((block, range));
        }
        var result = new StringBuilder(); var previousEnd = -1;
        foreach (var (block, range) in pieces)
        {
            if (result.Length > 0)
            {
                if (previousEnd <= range.Start && previousEnd >= 0 && source.AsSpan(previousEnd, range.Start - previousEnd).Trim().IsEmpty)
                    result.Append(source.AsSpan(previousEnd, range.Start - previousEnd));
                else result.Append("\n\n");
            }
            result.Append(Partial(source, block, range)); previousEnd = range.End;
        }
        // Reference-style links/images and footnotes need their definitions to remain usable.
        var definitions = new HashSet<SourceRange>();
        foreach (var (block, selected) in pieces)
            foreach (var container in Containers(block))
                foreach (var node in Nodes(container))
                {
                    if (!new SourceRange(node.Span.Start, node.Span.End + 1).Intersects(selected)) continue;
                    var definition = node switch { LinkInline { Reference: { } reference } => reference as Block,
                        FootnoteLink { IsBackLink: false } note => note.Footnote, _ => null };
                    if (definition is null) continue;
                    var span = DefinitionRange(source, definition);
                    if (span.Valid && !pieces.Any(p => p.Range.Start <= span.Start && p.Range.End >= span.End)) definitions.Add(span);
                }
        foreach (var definition in definitions.OrderBy(d => d.Start)) result.Append("\n\n").Append(source.AsSpan(definition.Start, definition.End - definition.Start));
        return result.ToString();
    }

    private static string Partial(string source, PreviewBlock block, SourceRange selected)
    {
        var whole = Range(block);
        if (IsAtomic(block) || whole == selected || block.Inline is null) return source[selected.Start..selected.End];
        var prefix = new StringBuilder(); var suffix = new StringBuilder();
        var first = block.Inline.FirstChild;
        while (first is Markdig.Extensions.TaskLists.TaskList) first = first.NextSibling;
        if (first is not null && first.Span.Start > whole.Start && selected.Start >= first.Span.Start)
        {
            var prefixEnd = first.Span.Start;
            while (prefixEnd < selected.Start && source[prefixEnd] is ' ' or '\t') prefixEnd++;
            prefix.Append(source.AsSpan(whole.Start, prefixEnd - whole.Start));
        }
        var emphasis = Nodes(block.Inline).OfType<EmphasisInline>().ToArray();
        var tags = Nodes(block.Inline).OfType<HtmlInline>().OrderBy(t => t.Span.Start).ToArray();
        var opening = emphasis.Where(e => e.Span.Start < selected.Start && selected.Start <= e.Span.End)
            .Select(e => (Start: e.Span.Start, Text: new string(e.DelimiterChar, e.DelimiterCount)))
            .Concat(OpenTags(tags, selected.Start).Select(t => (t.Start, Text: t.Raw)));
        var closing = emphasis.Where(e => e.Span.Start < selected.End && selected.End <= e.Span.End)
            .Select(e => (Start: e.Span.Start, Text: new string(e.DelimiterChar, e.DelimiterCount)))
            .Concat(OpenTags(tags, selected.End).Select(t => (t.Start, Text: "</" + t.Name + ">")));
        foreach (var wrapper in opening.OrderBy(t => t.Start)) prefix.Append(wrapper.Text);
        foreach (var wrapper in closing.OrderByDescending(t => t.Start)) suffix.Append(wrapper.Text);
        return prefix + source[selected.Start..selected.End] + suffix;
    }

    private static IEnumerable<(string Name, string Raw, int Start)> OpenTags(IEnumerable<HtmlInline> tags, int at)
    {
        var open = new List<(string Name, string Raw, int Start)>();
        foreach (var tag in tags)
        {
            if (tag.Span.Start >= at) break;
            var match = HtmlTag.Match(tag.Tag); if (!match.Success) continue;
            var name = match.Groups[2].Value;
            if (VoidTags.Contains(name) || tag.Tag.EndsWith("/>", StringComparison.Ordinal)) continue;
            if (match.Groups[1].Length == 0) open.Add((name, tag.Tag, tag.Span.Start));
            else
            {
                var index = open.FindLastIndex(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) open.RemoveRange(index, open.Count - index);
            }
        }
        return open;
    }

    public static IEnumerable<Inline> Nodes(ContainerInline container)
    {
        for (var node = container.FirstChild; node is not null; node = node.NextSibling)
        { yield return node; if (node is ContainerInline nested) foreach (var child in Nodes(nested)) yield return child; }
    }

    private static IEnumerable<ContainerInline> Containers(PreviewBlock block)
    {
        if (block.Inline is not null) yield return block.Inline;
        if (block.Cells is not null)
            foreach (var row in block.Cells) foreach (var cell in row) if (cell is not null) yield return cell;
    }

    private static SourceRange DefinitionRange(string source, Block block)
    {
        var start = Math.Clamp(block.Span.Start, 0, source.Length); var end = Math.Clamp(block.Span.End + 1, start, source.Length);
        return new(start == 0 ? 0 : source.LastIndexOf('\n', start - 1) + 1, end);
    }

    // Maps decoded glyph positions back through escapes/entities to original Markdown.
    public static int SourceOffset(string source, SourceRange range, int displayOffset)
    {
        var cursor = Math.Clamp(range.Start, 0, source.Length); var limit = Math.Clamp(range.End, cursor, source.Length);
        for (var glyph = 0; cursor < limit && glyph < displayOffset; glyph++)
        {
            if (source[cursor] == '\\' && cursor + 1 < limit && IsEscapable(source[cursor + 1])) cursor += 2;
            else if (source[cursor] == '&' && source.IndexOf(';', cursor, Math.Min(32, limit - cursor)) is var semicolon && semicolon > cursor)
            {
                var entity = source[cursor..(semicolon + 1)]; var decoded = WebUtility.HtmlDecode(entity);
                if (decoded != entity) { cursor = semicolon + 1; glyph += decoded.Length - 1; } else cursor++;
            }
            else cursor++;
        }
        return cursor;
    }

    public static int DisplayOffset(string source, SourceRange range, int offset, int textLength)
    {
        if (offset <= range.Start) return 0;
        if (offset >= range.End) return textLength;
        var low = 0; var high = textLength;
        while (low < high) { var middle = (low + high) / 2; if (SourceOffset(source, range, middle) < offset) low = middle + 1; else high = middle; }
        return low;
    }

    public static int Compare(ReadingPosition a, ReadingPosition b) => a.Block != b.Block ? a.Block.CompareTo(b.Block) : a.Offset.CompareTo(b.Offset);
    private static bool IsEscapable(char c) => c is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~';
    private static string Fence(string code, string language)
    {
        var longest = Regex.Matches(code, "`+").Select(m => m.Length).DefaultIfEmpty(0).Max(); var fence = new string('`', Math.Max(3, longest + 1));
        return fence + language + "\n" + code.TrimEnd('\r', '\n') + "\n" + fence;
    }
}
