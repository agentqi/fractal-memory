using System.Text.Json;
using System.Text.RegularExpressions;
using FractalMemory.Core.Domain.Models;

namespace FractalMemory.Core.Infrastructure.Parsing;

public static class DecisionLog
{
    private sealed record Marker(string Id, string Status, string RecordedAt, string? Supersedes);
    private static string Render(Marker marker) => "<!-- fractalmem-decision: " + JsonSerializer.Serialize(marker) + " -->";

    private static readonly Regex MarkerPattern = new(@"\A\s*<!-- fractalmem-decision: (.*?) -->", RegexOptions.Singleline | RegexOptions.Compiled);

    // Reads remain available after a hand edit; validation and every write use the strict parser.
    public static IReadOnlyList<DecisionEntry> Read(string content, bool hasFrontMatter = true)
    {
        try { return Parse(content, hasFrontMatter); }
        catch (InvalidOperationException) { return []; }
    }

    public static IReadOnlyList<DecisionEntry> Parse(string content, bool hasFrontMatter = true)
    {
        var entries = new List<DecisionEntry>();
        foreach (var section in MemoryMarkdown.HeadingSections(content, hasFrontMatter: hasFrontMatter))
        {
            var match = MarkerPattern.Match(section.Content);
            if (!match.Success) continue;
            Marker marker;
            try { marker = JsonSerializer.Deserialize<Marker>(match.Groups[1].Value) ?? throw new JsonException(); }
            catch (JsonException exception) { throw new InvalidOperationException("Malformed managed decision. Repair its metadata before appending.", exception); }
            if (string.IsNullOrWhiteSpace(marker.Id) || section.Title != $"Decision {marker.Id}" ||
                marker.Status is not ("active" or "superseded") || !DateTimeOffset.TryParse(marker.RecordedAt, out _))
                throw new InvalidOperationException("Decision heading, timestamp or status does not match its metadata.");
            entries.Add(new(marker.Id, marker.Status, marker.RecordedAt, marker.Supersedes, section.Content[match.Length..].Trim()));
        }
        if (Regex.Matches(content, @"<!--\s*fractalmem-decision:").Count != entries.Count)
            throw new InvalidOperationException("Malformed managed decision marker. Repair its metadata before updating decisions.");
        if (entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            throw new InvalidOperationException("Duplicate decision IDs must be repaired before updating decisions.");
        return entries;
    }

    public static string Append(string text, string content, string id, DateTimeOffset timestamp, string? supersedes)
    {
        if (MemoryMarkdown.Headings(content, hasFrontMatter: false).Any(h => h.Level <= 2))
            throw new ArgumentException("Decision content may only use headings at level 3 or deeper.");
        var entries = Parse(text);
        if (supersedes is not null)
        {
            var old = entries.SingleOrDefault(e => e.Id == supersedes && e.Status == "active")
                ?? throw new InvalidOperationException($"Active decision '{supersedes}' was not found. List decisions before superseding one.");
            var section = MemoryMarkdown.FindSection(text, $"Decision {old.Id}");
            var updated = Regex.Replace(section.Content, @"<!-- fractalmem-decision: .*? -->",
                _ => Render(new(old.Id, "superseded", old.RecordedAt, old.Supersedes)), RegexOptions.Singleline);
            text = MemoryMarkdown.ReplaceSection(text, $"Decision {old.Id}", updated);
        }
        return text.TrimEnd() + $"\n\n## Decision {id}\n\n" + Render(new(id, "active", timestamp.ToString("O"), supersedes)) + "\n\n" + content.Trim() + "\n";
    }
}
