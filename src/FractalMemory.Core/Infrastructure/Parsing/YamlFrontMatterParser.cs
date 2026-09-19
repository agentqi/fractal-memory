using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using FractalMemory.Core.Domain.Models;
using YamlDotNet.Serialization;

namespace FractalMemory.Core.Infrastructure.Parsing;

public sealed class YamlFrontMatterParser : IFrontMatterParser
{
    private static readonly Regex FrontMatterRegex = new(
        @"\A---\s*\r?\n(.*?)\r?\n---\s*\r?\n?",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly IDeserializer _deserializer = new DeserializerBuilder().Build();

    public ParsedMarkdownDocument Parse(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(markdown)));
        var normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var match = FrontMatterRegex.Match(normalized);
        if (!match.Success)
        {
            return new ParsedMarkdownDocument
            {
                Metadata = new NodeMetadata(),
                Content = normalized,
                HasFrontMatter = false,
                SourceHash = sourceHash,
            };
        }

        var yaml = match.Groups[1].Value;
        var body = normalized[match.Length..];
        var contentOffset = match.Length + body.Length - body.TrimStart().Length;

        Dictionary<object, object?>? data;
        try
        {
            data = _deserializer.Deserialize<Dictionary<object, object?>>(yaml);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException("Malformed front matter.", exception);
        }

        return new ParsedMarkdownDocument
        {
            Metadata = MapMetadata(data ?? []),
            Content = body.Trim(),
            HasFrontMatter = true,
            ContentStartLine = 1 + normalized[..contentOffset].Count(character => character == '\n'),
            SourceHash = sourceHash,
        };
    }

    public static string Render(NodeMetadata metadata)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        if (!string.IsNullOrWhiteSpace(metadata.Title))
        {
            builder.AppendLine($"title: {EscapeScalar(metadata.Title)}");
        }

        if (metadata.Aliases.Count > 0)
        {
            builder.AppendLine($"aliases: [{string.Join(", ", metadata.Aliases.Select(EscapeFlowItem))}]");
        }

        if (metadata.Tags.Count > 0)
        {
            builder.AppendLine($"tags: [{string.Join(", ", metadata.Tags.Select(EscapeFlowItem))}]");
        }

        builder.AppendLine($"status: {metadata.Status.ToString().ToLowerInvariant()}");
        builder.AppendLine($"priority: {metadata.Priority.ToString().ToLowerInvariant()}");
        if (metadata.LastUpdated is not null)
        {
            builder.AppendLine($"last_updated: {metadata.LastUpdated.Value:O}");
        }

        if (metadata.ReviewAfter is { } reviewAfter) builder.AppendLine($"review_after: {reviewAfter:O}");

        if (!string.IsNullOrWhiteSpace(metadata.Owner))
        {
            builder.AppendLine($"owner: {EscapeScalar(metadata.Owner)}");
        }

        if (!string.IsNullOrWhiteSpace(metadata.Summary))
        {
            builder.AppendLine($"summary: {EscapeScalar(metadata.Summary)}");
        }

        builder.AppendLine("---");
        return builder.ToString().TrimEnd();
    }

    private static NodeMetadata MapMetadata(IReadOnlyDictionary<object, object?> source)
    {
        var title = GetString(source, "title");
        var aliases = GetStringList(source, "aliases");
        var tags = GetStringList(source, "tags");
        var summary = GetString(source, "summary");
        var owner = GetString(source, "owner");
        var status = ParseEnum(source, "status", NodeStatus.Active);
        var priority = ParseEnum(source, "priority", PriorityLevel.Medium);
        var lastUpdated = ParseDate(source, "last_updated");

        return new NodeMetadata
        {
            Title = title,
            Aliases = aliases,
            Tags = tags,
            Summary = summary,
            Owner = owner,
            Status = status,
            Priority = priority,
            LastUpdated = lastUpdated,
            ReviewAfter = ParseDate(source, "review_after"),
        };
    }

    private static string EscapeScalar(string value)
    {
        if (NeedsQuoting(value, includeFlowReservedChars: false))
        {
            return Quote(value);
        }

        return value;
    }

    private static string EscapeFlowItem(string value)
    {
        if (NeedsQuoting(value, includeFlowReservedChars: true))
        {
            return Quote(value);
        }

        return value;
    }

    private static readonly HashSet<string> YamlReservedLiterals = new(StringComparer.OrdinalIgnoreCase)
    {
        "true", "false", "null", "yes", "no", "on", "off", "y", "n", "~",
    };

    private static bool NeedsQuoting(string value, bool includeFlowReservedChars)
    {
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        if (value != value.Trim())
        {
            return true;
        }

        if (value.IndexOfAny(['\r', '\n']) >= 0)
        {
            return true;
        }

        if (YamlReservedLiterals.Contains(value))
        {
            return true;
        }

        if (LooksLikeYamlNumber(value))
        {
            return true;
        }

        foreach (var ch in value)
        {
            switch (ch)
            {
                case ':':
                case '#':
                case '"':
                case '\'':
                case '\\':
                case '&':
                case '*':
                case '!':
                case '|':
                case '>':
                case '%':
                case '@':
                case '`':
                    return true;
                case '[':
                case ']':
                case '{':
                case '}':
                case ',':
                    if (includeFlowReservedChars)
                    {
                        return true;
                    }

                    break;
            }
        }

        var first = value[0];
        if (first is '-' or '?' or ':' or '[' or ']' or '{' or '}' or '#' or '&' or '*' or '!' or '|' or '>' or '%' or '@' or '`' or '"' or '\'')
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeYamlNumber(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static string Quote(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static string? GetString(IReadOnlyDictionary<object, object?> source, string key) =>
        source.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static IReadOnlyList<string> GetStringList(IReadOnlyDictionary<object, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        if (value is IEnumerable<object> enumerable)
        {
            return enumerable.Select(item => item.ToString()).Where(item => !string.IsNullOrWhiteSpace(item)).Cast<string>().ToArray();
        }

        var raw = value.ToString();
        return string.IsNullOrWhiteSpace(raw) ? [] : [raw];
    }

    private static TEnum ParseEnum<TEnum>(IReadOnlyDictionary<object, object?> source, string key, TEnum fallback)
        where TEnum : struct
    {
        if (!source.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        return Enum.TryParse<TEnum>(value.ToString(), true, out var parsed) ? parsed : fallback;
    }

    private static DateTimeOffset? ParseDate(IReadOnlyDictionary<object, object?> source, string key)
    {
        if (!source.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
