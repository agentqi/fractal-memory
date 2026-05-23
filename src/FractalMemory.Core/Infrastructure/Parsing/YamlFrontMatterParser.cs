using System.Globalization;
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

        var match = FrontMatterRegex.Match(markdown);
        if (!match.Success)
        {
            return new ParsedMarkdownDocument
            {
                Metadata = new NodeMetadata(),
                Content = markdown,
                HasFrontMatter = false,
            };
        }

        var yaml = match.Groups[1].Value;
        var body = markdown[match.Length..];

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
        };
    }

    public static string Render(NodeMetadata metadata)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        if (!string.IsNullOrWhiteSpace(metadata.Title))
        {
            builder.AppendLine($"title: {Escape(metadata.Title)}");
        }

        if (metadata.Aliases.Count > 0)
        {
            builder.AppendLine($"aliases: [{string.Join(", ", metadata.Aliases.Select(Escape))}]");
        }

        if (metadata.Tags.Count > 0)
        {
            builder.AppendLine($"tags: [{string.Join(", ", metadata.Tags.Select(Escape))}]");
        }

        builder.AppendLine($"status: {metadata.Status.ToString().ToLowerInvariant()}");
        builder.AppendLine($"priority: {metadata.Priority.ToString().ToLowerInvariant()}");
        if (metadata.LastUpdated is not null)
        {
            builder.AppendLine($"last_updated: {metadata.LastUpdated.Value:O}");
        }

        if (!string.IsNullOrWhiteSpace(metadata.Owner))
        {
            builder.AppendLine($"owner: {Escape(metadata.Owner)}");
        }

        if (!string.IsNullOrWhiteSpace(metadata.Summary))
        {
            builder.AppendLine($"summary: {Escape(metadata.Summary)}");
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
        };
    }

    private static string Escape(string value) =>
        value.Contains(':', StringComparison.Ordinal) || value.Contains('[', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;

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
