using System.Net;
using System.Text.RegularExpressions;

namespace FractalMemory.Core.Infrastructure.Parsing;

internal static class HtmlTextExtractor
{
    private static readonly Regex ScriptOrStyleRegex = new(
        "<(script|style)[^>]*>.*?</\\1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled);
    private static readonly Regex TitleRegex = new(
        "<title[^>]*>(.*?)</title>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex HeadingRegex = new(
        "<h1[^>]*>(.*?)</h1>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public static string ToText(string html)
    {
        var withoutScripts = ScriptOrStyleRegex.Replace(html, " ");
        var withoutTags = TagRegex.Replace(withoutScripts, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return WhitespaceRegex.Replace(decoded, " ").Trim();
    }

    public static string? ExtractTitle(string html)
    {
        var title = ExtractFirstMatch(html, TitleRegex) ?? ExtractFirstMatch(html, HeadingRegex);
        return string.IsNullOrWhiteSpace(title) ? null : title;
    }

    private static string? ExtractFirstMatch(string html, Regex regex)
    {
        var match = regex.Match(html);
        return match.Success ? ToText(match.Groups[1].Value) : null;
    }
}
