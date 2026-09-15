using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace FractalMemory.Core.Infrastructure.Parsing;

/// <summary>Source-aware ATX headings. Front matter, comments and fenced code are not headings.</summary>
public static class MemoryMarkdown
{
    public sealed record Heading(string Title, int Level, int Line);
    public sealed record Section(string Title, int StartLine, int EndLine, string Content);
    public static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static IReadOnlyList<Heading> Headings(string text)
    {
        var lines = Normalize(text).Split('\n');
        var headings = new List<Heading>();
        char fence = '\0';
        var fenceLength = 0;
        var frontMatter = lines[0].TrimEnd() == "---";
        var comment = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (frontMatter)
            {
                if (i > 0 && line.TrimEnd() == "---") frontMatter = false;
                continue;
            }
            var matchFence = Regex.Match(line, @"^ {0,3}(`{3,}|~{3,})(.*)$");
            if (fence != '\0')
            {
                if (matchFence.Success && matchFence.Groups[1].Value[0] == fence &&
                    matchFence.Groups[1].Length >= fenceLength && string.IsNullOrWhiteSpace(matchFence.Groups[2].Value)) fence = '\0';
                continue;
            }
            if (comment)
            {
                comment = !line.Contains("-->", StringComparison.Ordinal);
                continue;
            }
            if (matchFence.Success)
            {
                fence = matchFence.Groups[1].Value[0];
                fenceLength = matchFence.Groups[1].Length;
                continue;
            }
            if (line.Contains("<!--", StringComparison.Ordinal))
            {
                comment = !line.Contains("-->", StringComparison.Ordinal);
                continue;
            }
            var match = Regex.Match(line, @"^ {0,3}(#{1,6})[ \t]+(.+?)\s*$");
            if (match.Success) headings.Add(new(Regex.Replace(match.Groups[2].Value, @"[ \t]+#+[ \t]*$", "").Trim(), match.Groups[1].Length, i + 1));
        }
        return headings;
    }

    public static IReadOnlyList<Section> Sections(string text)
    {
        var lines = Normalize(text).Split('\n');
        var headings = Headings(text);
        var sections = new List<Section>();
        var start = 1;
        var title = "Document";
        foreach (var heading in headings)
        {
            if (heading.Line > start) sections.Add(new(title, start, heading.Line - 1, string.Join('\n', lines[(start - 1)..(heading.Line - 1)])));
            start = heading.Line + 1;
            title = heading.Title;
        }
        if (start <= lines.Length) sections.Add(new(title, start, lines.Length, string.Join('\n', lines[(start - 1)..])));
        return sections;
    }

    public static Section FindSection(string text, string title)
    {
        var lines = Normalize(text).Split('\n');
        var headings = Headings(text);
        var matches = headings.Where(h => h.Title.Equals(title, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException(matches.Length == 0
            ? $"Section '{title}' was not found. Read the document to choose an existing heading."
            : $"Section '{title}' is ambiguous; rename duplicate headings before updating it.");
        var heading = matches[0];
        var end = headings.FirstOrDefault(h => h.Line > heading.Line && h.Level <= heading.Level)?.Line - 1 ?? lines.Length;
        return new(title, heading.Line + 1, end, string.Join('\n', lines[heading.Line..end]));
    }

    public static string ReplaceSection(string text, string title, string content)
    {
        var section = FindSection(text, title);
        var lines = Normalize(text).Split('\n');
        var replacement = Normalize(content).Trim();
        var level = Headings(text).Single(h => h.Line == section.StartLine - 1).Level;
        if (Headings(replacement).Any(h => h.Level <= level))
            throw new ArgumentException("Section content cannot introduce a heading at or above the selected section's level.");
        return string.Join('\n', lines[..(section.StartLine - 1)]) + "\n\n" + replacement + "\n\n" + string.Join('\n', lines[section.EndLine..]);
    }

    public static Dictionary<string, object?> Metadata(string text)
    {
        var match = Regex.Match(Normalize(text), @"\A---[ \t]*\n(.*?\n)?---[ \t]*(?:\n|$)", RegexOptions.Singleline);
        if (!match.Success) return [];
        try { return new DeserializerBuilder().Build().Deserialize<Dictionary<string, object?>>(match.Groups[1].Value) ?? []; }
        catch (YamlDotNet.Core.YamlException exception) { throw new InvalidOperationException("Malformed front matter.", exception); }
    }

    public static string SetMetadata(string text, IReadOnlyDictionary<string, object?> changes)
    {
        var metadata = Metadata(text);
        foreach (var change in changes) metadata[change.Key] = change.Value;
        var normalized = Normalize(text);
        var match = Regex.Match(normalized, @"\A---[ \t]*\n(.*?\n)?---[ \t]*(?:\n|$)", RegexOptions.Singleline);
        var body = match.Success ? normalized[match.Length..] : normalized;
        return "---\n" + new SerializerBuilder().Build().Serialize(metadata).TrimEnd() + "\n---\n" + body;
    }

    // Exact legacy scaffold text is guidance, never evidence of a real objective or decision.
    public static string Knowledge(string text) => string.Join('\n', Regex.Replace(Normalize(text), @"<!--.*?-->", "", RegexOptions.Singleline)
        .Split('\n').Where(line => !Placeholders.Contains(line.Trim().TrimStart('-', '*', ' '))));

    private static readonly HashSet<string> Placeholders = new(StringComparer.Ordinal)
    {
        "Capture what is currently true, active, and important.", "Capture what is true right now.",
        "Capture the current operating reality, active priorities, and known constraints here.",
        "Add constraints, guardrails, or decisions in force.", "Add the active constraints that apply right now.",
        "Add the next best actions.", "Add the next best actions to move the repository forward.",
        "Add unresolved questions.", "Add unresolved questions that block progress.",
        "Add dated entries here.", "Add dated updates here.",
        "Record decisions and rationale here.", "Record key decisions and why they were made.",
    };
}
