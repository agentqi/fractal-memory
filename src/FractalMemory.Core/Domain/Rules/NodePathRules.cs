using System.Text.RegularExpressions;

namespace FractalMemory.Core.Domain.Rules;

public static partial class NodePathRules
{
    public static string Normalize(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        var normalized = input.Trim().Replace('\\', '/').Trim('/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        Validate(normalized);
        return normalized;
    }

    public static void Validate(string normalizedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedPath);

        var segments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            throw new InvalidOperationException("Node path cannot be empty.");
        }

        foreach (var segment in segments)
        {
            if (segment is "." or "..")
            {
                throw new InvalidOperationException("Relative traversal is not allowed in node paths.");
            }

            if (!AllowedSegmentRegex().IsMatch(segment))
            {
                throw new InvalidOperationException(
                    $"Invalid node path segment '{segment}'. Use lowercase letters, numbers, and hyphens only.");
            }
        }
    }

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex AllowedSegmentRegex();
}
