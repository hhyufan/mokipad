using System.Text.RegularExpressions;

namespace MokiPad.Core;

public sealed record CodeToken(int Offset, int Length, string Kind);

/// <summary>Bounded lexical highlighting shared by preview and verification. No UI objects or whole-document scan.</summary>
public static class CodeSyntax
{
    private static readonly Regex Lexer = new(
        "(?<comment>//[^\\r\\n]*|/\\*[\\s\\S]*?(?:\\*/|$))|(?<string>\"(?:\\\\.|[^\"\\\\])*\"|'(?:\\\\.|[^'\\\\])*'|`(?:\\\\.|[^`\\\\])*`)|(?<variable>\\$[\\w:{}/.-]+)|(?<number>\\b(?:0[xX][0-9a-fA-F]+|\\d+(?:\\.\\d+)?)\\b)|(?<word>[\\p{L}_][\\p{L}\\p{N}_-]*)|(?<operator>=>|[+*/%=!<>|&?:-]+)|(?<hash>#[^\\r\\n]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "const", "let", "var", "new", "return", "if", "else", "for", "foreach", "while", "class", "public", "private",
        "protected", "static", "void", "async", "await", "using", "import", "from", "def", "function", "try", "catch",
        "finally", "throw", "namespace", "interface", "export", "default", "in", "as", "is", "switch", "case", "break",
        "continue", "pass", "yield", "with", "lambda", "param", "begin", "process", "end"
    };

    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    {
        "string", "int", "bool", "double", "float", "long", "decimal", "object", "Task", "List", "Dictionary",
        "Console", "String", "Number", "Boolean", "Date", "Math", "JSON", "Promise", "print", "console"
    };

    public static IReadOnlyList<CodeToken> Tokenize(string source, string language)
    {
        var lang = language.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant() ??
                   "";
        if (lang is "" or "text" or "txt" or "plain" or "plaintext") return [];
        var shell = lang is "powershell" or "pwsh" or "ps1" or "ps" or "bash" or "sh" or "shell" or "zsh" or "console"
            or "cmd" or "bat" or "batch";
        var result = new List<CodeToken>();
        try
        {
            foreach (Match match in Lexer.Matches(source))
            {
                string? kind = null;
                if (match.Groups["comment"].Success ||
                    (match.Groups["hash"].Success && (shell || lang is "python" or "py" or "yaml" or "yml" or "ruby" or "rb")))
                {
                    kind = "Comment";
                }
                else if (match.Groups["string"].Success)
                {
                    kind = "String";
                }
                else if (match.Groups["variable"].Success)
                {
                    kind = "Variable";
                }
                else if (match.Groups["number"].Success ||
                         match.Value is "true" or "false" or "null" or "None" or "True" or "False")
                {
                    kind = "Number";
                }
                else if (Keywords.Contains(match.Value))
                {
                    kind = "Keyword";
                }
                else if (Types.Contains(match.Value))
                {
                    kind = "Type";
                }
                else if (match.Groups["word"].Success)
                {
                    var tail = match.Index + match.Length;
                    while (tail < source.Length && source[tail] is ' ' or '\t') tail++;
                    kind = (shell, CommandPosition(source, match.Index), match.Index > 0 && source[match.Index - 1] == '-', tail < source.Length && source[tail] == '(') switch
                    {
                        (true, true, _, _) => "Command",
                        (true, false, true, _) => "Parameter",
                        (_, _, _, true) => "Function",
                        _ => kind
                    };
                }
                else if (match.Groups["operator"].Success)
                {
                    kind = "Operator";
                }

                if (kind is not null) result.Add(new CodeToken(match.Index, match.Length, kind));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            /* Keep the original text if a pathological chunk exceeds its budget. */
        }

        return result;
    }

    private static bool CommandPosition(string source, int offset)
    {
        for (var index = offset - 1; index >= 0; index--)
        {
            if (source[index] is '\r' or '\n' or '|' or ';' or '&') return true;
            if (!char.IsWhiteSpace(source[index])) return false;
        }

        return true;
    }
}