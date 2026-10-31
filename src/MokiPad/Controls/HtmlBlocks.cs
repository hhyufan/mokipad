using System.Text;
using System.Text.RegularExpressions;

namespace MokiPad.Controls;

internal sealed record HtmlPiece(string Kind, string Align, string Html);

/// <summary>
///     Splits a raw HTML block into block-level pieces (paragraph, heading, list item, rule)
///     while leaving inline markup untouched for <see cref="HtmlContext" />.
/// </summary>
internal static class HtmlBlocks
{
    private static readonly Regex TagPattern = new("<[^>]*>", RegexOptions.Compiled);

    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "center", "h1", "h2", "h3", "h4", "h5", "h6",
        "li", "ul", "ol", "pre", "blockquote", "details", "summary", "hr", "table", "tr", "td", "th",
        "figure", "figcaption"
    };

    private static readonly HashSet<string> HeadingTags = new(StringComparer.OrdinalIgnoreCase)
        { "h1", "h2", "h3", "h4", "h5", "h6" };

    private static readonly HashSet<string> KeepKind = new(StringComparer.OrdinalIgnoreCase)
        { "li", "pre", "blockquote", "summary" };

    public static List<HtmlPiece> Parse(string html)
    {
        var pieces = new List<HtmlPiece>();
        var buffer = new StringBuilder();
        var stack = new Stack<Frame>();
        var current = new Frame("p", "");

        var position = 0;
        foreach (Match match in TagPattern.Matches(html))
        {
            if (match.Index > position) buffer.Append(html, position, match.Index - position);
            position = match.Index + match.Length;
            var tag = match.Value;
            var closing = tag.StartsWith("</", StringComparison.Ordinal);
            var body = tag.Trim('<', '>', ' ').TrimStart('/').TrimEnd('/').Trim();
            var name = body.Split(' ', '\t', '\r', '\n')[0].ToLowerInvariant();
            if (!BlockTags.Contains(name))
            {
                buffer.Append(tag);
                continue;
            }

            if (name == "hr")
            {
                Flush();
                pieces.Add(new HtmlPiece("rule", current.Align, ""));
                continue;
            }

            if (closing)
            {
                if (stack.Count > 0)
                {
                    Flush();
                    current = stack.Pop();
                }

                continue;
            }

            Flush();
            stack.Push(current);
            var align = Attribute(tag, "align") ?? current.Align;
            var kind = HeadingTags.Contains(name) ? name : KeepKind.Contains(name) ? name : "p";
            current = new Frame(kind, align);
            if (tag.EndsWith("/>", StringComparison.Ordinal) && stack.Count > 0) current = stack.Pop();
        }

        if (position < html.Length) buffer.Append(html, position, html.Length - position);
        Flush();
        return pieces;
        void Flush()
        {
            var value = buffer.ToString();
            buffer.Clear();
            if (value.Trim().Length > 0) pieces.Add(new HtmlPiece(current.Kind, current.Align, value));
        }

    }

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, "\\b" + name + "\\s*=\\s*(\"([^\"]*)\"|'([^']*)'|([^\\s>]+))",
            RegexOptions.IgnoreCase);
        if (!match.Success) return null;
        return match.Groups[2].Success ? match.Groups[2].Value :
            match.Groups[3].Success ? match.Groups[3].Value : match.Groups[4].Value;
    }

    private sealed record Frame(string Kind, string Align);

}