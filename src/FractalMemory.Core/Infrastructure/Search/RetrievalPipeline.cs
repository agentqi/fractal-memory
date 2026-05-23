using System.Globalization;
using System.Text.RegularExpressions;
using FractalMemory.Core.Domain.Models;

namespace FractalMemory.Core.Infrastructure.Search;

internal sealed record RetrievalQueryProfile(
    string OriginalQuery,
    string LoweredQuery,
    IReadOnlyList<string> Terms,
    IReadOnlyList<string> Phrases,
    IReadOnlyList<string> PathSegments,
    IReadOnlyList<DateOnly> Dates,
    bool AsksWhen);

internal sealed record MarkdownSection(string Heading, int StartLine, int EndLine, IReadOnlyList<string> Lines);

internal sealed record SnippetCandidate(
    string MatchedFile,
    string SourcePath,
    string Snippet,
    string? SectionHeading,
    int StartLine,
    int EndLine,
    IReadOnlyDictionary<string, int> Breakdown)
{
    public int Score => Breakdown.Values.Sum();
}

internal static partial class RetrievalPipeline
{
    private static readonly HashSet<string> StopTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "from", "that", "this", "what", "when", "where", "which", "who", "did", "does",
        "was", "were", "have", "has", "had", "into", "onto", "about", "them", "they", "then", "show", "only", "latest",
    };

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+(.*)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(
        @"\b(?:" +
            @"\d{4}-\d{1,2}-\d{1,2}" +
            @"|\d{4}/\d{1,2}/\d{1,2}" +
            @"|\d{1,2}/\d{1,2}/\d{2,4}" +
            @"|\d{1,2}-\d{1,2}-\d{2,4}" +
            @"|\d{1,2}\s+(?:january|february|march|april|may|june|july|august|september|october|november|december|jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)\s*,?\s*\d{2,4}" +
            @"|(?:january|february|march|april|may|june|july|august|september|october|november|december|jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)\s+\d{1,2}\s*,?\s*\d{2,4}" +
        @")\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"[^a-z0-9/\-\s]")]
    private static partial Regex NonAlphaRegex();

    private static readonly string[] TemporalQueryMarkers =
    {
        "when",
        "what date",
        "what time",
        "by when",
        "how long ago",
        "which day",
        "which week",
        "which month",
        "earliest",
        "latest",
        "most recent",
        "before",
        "after",
        "since",
        "until",
    };

    public static RetrievalQueryProfile BuildQueryProfile(string query)
    {
        var lowered = query.Trim().ToLowerInvariant();
        var rawTerms = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var contentTerms = rawTerms.Where(IsContentTerm).ToArray();
        var terms = contentTerms.Distinct(StringComparer.Ordinal).ToArray();
        var phrases = BuildPhrases(contentTerms);
        var pathSegments = lowered.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var dates = ExtractDates(query);
        var asksWhen = TemporalQueryMarkers.Any(marker => lowered.Contains(marker, StringComparison.Ordinal));
        return new RetrievalQueryProfile(query, lowered, terms, phrases, pathSegments, dates, asksWhen);
    }

    private static bool IsContentTerm(string term) =>
        term.Length > 2 && !StopTerms.Contains(term);

    public static IReadOnlyList<MarkdownSection> ParseSections(string markdown)
    {
        var normalized = NormalizeLineEndings(markdown);
        var lines = normalized.Split('\n');
        var sections = new List<MarkdownSection>();
        string currentHeading = "Document";
        var currentStart = 1;
        var buffer = new List<string>();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var match = HeadingRegex().Match(line);
            if (match.Success)
            {
                if (buffer.Count > 0)
                {
                    sections.Add(new MarkdownSection(currentHeading, currentStart, index, buffer.ToArray()));
                }

                currentHeading = match.Groups[1].Value.Trim();
                currentStart = index + 2;
                buffer = new List<string>();
                continue;
            }

            buffer.Add(line);
        }

        if (buffer.Count > 0)
        {
            sections.Add(new MarkdownSection(currentHeading, currentStart, lines.Length, buffer.ToArray()));
        }

        return sections;
    }

    public static IReadOnlyList<SnippetCandidate> BuildSearchCandidates(
        RetrievalQueryProfile profile,
        MemoryNode node,
        string title,
        string fileName,
        string content,
        int maxSnippetLines)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var sourcePath = $"{node.RelativePath}/{fileName}";
        var sections = ParseSections(content);
        var candidates = new List<SnippetCandidate>();

        foreach (var section in sections)
        {
            var scoredLines = ScoreSectionLines(profile, section);
            var selected = scoredLines
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.LineNumber)
                .Take(2)
                .ToArray();

            foreach (var item in selected)
            {
                var startLine = Math.Max(section.StartLine, item.LineNumber - 1);
                var endLine = Math.Min(section.EndLine, startLine + maxSnippetLines - 1);
                var snippetLines = section.Lines.Skip(startLine - section.StartLine).Take(endLine - startLine + 1)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToArray();
                if (snippetLines.Length == 0)
                {
                    continue;
                }

                var breakdown = BuildBreakdown(profile, node, title, fileName, section, string.Join(Environment.NewLine, snippetLines));
                if (breakdown.Values.Sum() <= 0)
                {
                    continue;
                }

                candidates.Add(new SnippetCandidate(
                    fileName,
                    sourcePath,
                    string.Join(Environment.NewLine, snippetLines).Trim(),
                    section.Heading,
                    startLine,
                    endLine,
                    breakdown));
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.SourcePath, StringComparer.Ordinal)
            .DistinctBy(candidate => $"{candidate.SourcePath}|{candidate.SectionHeading}|{candidate.Snippet}")
            .ToArray();
    }

    public static IReadOnlyList<SearchResult> BuildNodeEvidenceResults(MemoryNode node, int maxSnippetLines)
    {
        var title = node.Metadata.Title ?? Path.GetFileName(node.RelativePath);
        var evidence = new List<SearchResult>();

        AddEvidence(evidence, node, title, node.StateFileName, node.StateContent, "Current State", 320, maxSnippetLines);
        AddEvidence(evidence, node, title, node.DecisionsFileName, node.DecisionsContent, "Decisions", 280, maxSnippetLines);
        AddEvidence(evidence, node, title, node.IndexFileName, node.IndexContent, "Summary", 220, maxSnippetLines);
        AddEvidence(evidence, node, title, node.TimelineFileName, node.TimelineContent, "Timeline", 180, maxSnippetLines);

        return evidence
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.SourcePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddEvidence(
        List<SearchResult> evidence,
        MemoryNode node,
        string title,
        string fileName,
        string content,
        string preferredHeading,
        int filePrior,
        int maxSnippetLines)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var sections = ParseSections(content);
        var section = sections.FirstOrDefault(item => item.Heading.Contains(preferredHeading, StringComparison.OrdinalIgnoreCase))
            ?? sections.FirstOrDefault(item => item.Lines.Any(line => !string.IsNullOrWhiteSpace(line)));
        if (section is null)
        {
            return;
        }

        var lines = section.Lines
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Take(maxSnippetLines)
            .ToArray();
        if (lines.Length == 0)
        {
            return;
        }

        evidence.Add(new SearchResult
        {
            RelativePath = node.RelativePath,
            Title = title,
            MatchedFile = fileName,
            SourcePath = $"{node.RelativePath}/{fileName}",
            Snippet = string.Join(Environment.NewLine, lines),
            SectionHeading = section.Heading,
            StartLine = section.StartLine,
            EndLine = Math.Min(section.EndLine, section.StartLine + lines.Length - 1),
            Score = filePrior,
            ScoreBreakdown = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["file_type_prior"] = filePrior,
            },
        });
    }

    private static IReadOnlyList<(int LineNumber, int Score)> ScoreSectionLines(RetrievalQueryProfile profile, MarkdownSection section)
    {
        var scored = new List<(int LineNumber, int Score)>();
        for (var index = 0; index < section.Lines.Count; index++)
        {
            var line = section.Lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var score = ScoreLine(profile, line, section.Heading);
            if (score > 0)
            {
                scored.Add((section.StartLine + index, score));
            }
        }

        return scored;
    }

    private static IReadOnlyDictionary<string, int> BuildBreakdown(
        RetrievalQueryProfile profile,
        MemoryNode node,
        string title,
        string fileName,
        MarkdownSection section,
        string snippet)
    {
        var snippetNormalized = Normalize(snippet);
        var breakdown = new Dictionary<string, int>(StringComparer.Ordinal);

        var entityOverlap = profile.Terms.Count(term => snippetNormalized.Contains(term, StringComparison.Ordinal));
        if (entityOverlap > 0)
        {
            breakdown["entity_overlap"] = entityOverlap * 55;
        }

        var phraseMatches = profile.Phrases.Count(phrase => snippetNormalized.Contains(phrase, StringComparison.Ordinal));
        if (phraseMatches > 0)
        {
            breakdown["event_phrase_overlap"] = phraseMatches * 90;
        }

        if (profile.PathSegments.Any(segment => node.RelativePath.Contains(segment, StringComparison.OrdinalIgnoreCase)))
        {
            breakdown["path_prior"] = 80;
        }

        var snippetDates = ExtractDates(snippet);
        if (profile.Dates.Count > 0 && profile.Dates.Intersect(snippetDates).Any())
        {
            breakdown["exact_date_match"] = 220;
        }
        else if (profile.AsksWhen && snippetDates.Count > 0)
        {
            breakdown["normalized_date_match"] = 150;
        }

        var hasContentSignal =
            entityOverlap > 0 ||
            phraseMatches > 0 ||
            breakdown.ContainsKey("exact_date_match") ||
            breakdown.ContainsKey("normalized_date_match");

        var filePrior = hasContentSignal
            ? Path.GetFileNameWithoutExtension(fileName) switch
            {
                "state" => 160,
                "decisions" => 140,
                "index" => 110,
                "timeline" => 70,
                _ when fileName.StartsWith("artifacts/", StringComparison.Ordinal) => 40,
                _ => 0,
            }
            : 0;
        if (filePrior > 0)
        {
            breakdown["file_type_prior"] = filePrior;
        }

        if (profile.AsksWhen && snippetDates.Count > 0 && phraseMatches > 0)
        {
            breakdown["date_event_cooccurrence"] = 140;
        }

        if (profile.AsksWhen && snippetDates.Count == 0 && entityOverlap > 0)
        {
            breakdown["temporal_near_miss_penalty"] = -120;
        }

        if (profile.Phrases.Count > 0 &&
            phraseMatches == 0 &&
            entityOverlap > 0)
        {
            breakdown["thematic_only_penalty"] = -60;
        }

        if (section.Heading.Contains("timeline", StringComparison.OrdinalIgnoreCase) && profile.AsksWhen)
        {
            breakdown["timeline_date_bonus"] = 30;
        }

        return breakdown;
    }

    private static int ScoreLine(RetrievalQueryProfile profile, string line, string heading)
    {
        var normalized = Normalize(line);
        var score = 0;
        foreach (var term in profile.Terms)
        {
            if (normalized.Contains(term, StringComparison.Ordinal))
            {
                score += 40;
            }
        }

        foreach (var phrase in profile.Phrases)
        {
            if (normalized.Contains(phrase, StringComparison.Ordinal))
            {
                score += 70;
            }
        }

        if (profile.AsksWhen && DateRegex().IsMatch(line))
        {
            score += 60;
        }

        if (heading.Contains("decision", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        return score;
    }

    private static IReadOnlyList<string> BuildPhrases(IReadOnlyList<string> contentTerms)
    {
        var phrases = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var size = 3; size >= 2; size--)
        {
            for (var index = 0; index <= contentTerms.Count - size; index++)
            {
                var phrase = string.Join(' ', contentTerms.Skip(index).Take(size));
                if (seen.Add(phrase))
                {
                    phrases.Add(phrase);
                }
            }
        }

        return phrases;
    }

    internal static IReadOnlyList<DateOnly> ExtractDatesForHighlight(string text) => ExtractDates(text);

    private static IReadOnlyList<DateOnly> ExtractDates(string text)
    {
        var dates = new List<DateOnly>();
        foreach (Match match in DateRegex().Matches(text))
        {
            if (DateTime.TryParse(match.Value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            {
                dates.Add(DateOnly.FromDateTime(parsed));
            }
        }

        return dates.Distinct().ToArray();
    }

    private static string Normalize(string text) =>
        NonAlphaRegex().Replace(text.ToLowerInvariant(), " ").Trim();

    private static string NormalizeLineEndings(string markdown) =>
        markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
