using System.Text.Json;
using System.Text.RegularExpressions;
using FractalMemory.Core.Domain.Models;

namespace FractalMemory.Core.Infrastructure.Parsing;

public static class DecisionLog
{
    private sealed record Marker(string Id, string Status, string RecordedAt, string? Supersedes);
    private static string Render(Marker marker) => "<!-- fractalmem-decision: " + JsonSerializer.Serialize(marker) + " -->";

    public static IReadOnlyList<DecisionEntry> Parse(string content)
    {
        var entries = new List<DecisionEntry>();
        foreach (var heading in MemoryMarkdown.Headings(content).Where(h => h.Level == 2 && h.Title.StartsWith("Decision ", StringComparison.Ordinal)))
        {
            var section = MemoryMarkdown.FindSection(content, heading.Title);
            var match = Regex.Match(section.Content, @"\A\s*<!-- fractalmem-decision: (.*?) -->", RegexOptions.Singleline);
            if (!match.Success) continue;
            Marker marker;
            try { marker = JsonSerializer.Deserialize<Marker>(match.Groups[1].Value) ?? throw new JsonException(); }
            catch (JsonException exception) { throw new InvalidOperationException("Malformed managed decision. Repair its metadata before appending.", exception); }
            if (heading.Title != $"Decision {marker.Id}" || marker.Status is not ("active" or "superseded"))
                throw new InvalidOperationException("Decision heading or status does not match its metadata.");
            entries.Add(new(marker.Id, marker.Status, marker.RecordedAt, marker.Supersedes, section.Content[match.Length..].Trim()));
        }
        if (entries.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            throw new InvalidOperationException("Duplicate decision IDs must be repaired before updating decisions.");
        return entries;
    }

    public static string Append(string text, string content, string id, DateTimeOffset timestamp, string? supersedes)
    {
        if (MemoryMarkdown.Headings(content).Any(h => h.Level <= 2))
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
