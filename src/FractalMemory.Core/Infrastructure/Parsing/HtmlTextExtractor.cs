using System.Net;
using System.Text.RegularExpressions;

namespace FractalMemory.Core.Infrastructure.Parsing;

internal static class HtmlTextExtractor
{
    private static readonly Regex ScriptOrStyleRegex = new(
        @"<(script|style|head)\b[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled);
    private static readonly Regex TitleRegex = new(
        "<title[^>]*>(.*?)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HeadingRegex = new(
        "<h1[^>]*>(.*?)</h1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SectionHeadingRegex = new(
        @"<h([1-6])\b[^>]*>(.*?)</h\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex BlockRegex = new(
        @"</?(?:p|div|section|article|main|header|footer|ul|ol|table|tr|blockquote|pre)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ListItemRegex = new(@"<li\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LineBreakRegex = new(@"</li\s*>|<br\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CommentRegex = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);

    public static string ToText(string html)
    {
        // Preserve the same headings and lists consumed by Markdown retrieval and state parsing.
        var content = ScriptOrStyleRegex.Replace(CommentRegex.Replace(html, ""), " ");
        content = WhitespaceRegex.Replace(content, " ");
        content = SectionHeadingRegex.Replace(content, match =>
            $"\n\n{new string('#', int.Parse(match.Groups[1].Value))} {match.Groups[2].Value}\n\n");
        content = BlockRegex.Replace(content, "\n\n");
        content = ListItemRegex.Replace(content, "\n- ");
        content = LineBreakRegex.Replace(content, "\n");
        content = WebUtility.HtmlDecode(TagRegex.Replace(content, ""));
        content = string.Join('\n', content.Split('\n').Select(line => line.Trim()));
        return Regex.Replace(content, @"\n{3,}", "\n\n").Trim();
    }

    public static string? ExtractTitle(string html)
    {
        var title = ExtractFirstMatch(html, TitleRegex) ?? ExtractFirstMatch(html, HeadingRegex);
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static string? ExtractFirstMatch(string html, Regex regex)
    {
        var match = regex.Match(html);
        return match.Success ? InlineText(match.Groups[1].Value) : null;
    }

    private static string InlineText(string html) =>
        WhitespaceRegex.Replace(WebUtility.HtmlDecode(TagRegex.Replace(html, "")), " ").Trim();
}
