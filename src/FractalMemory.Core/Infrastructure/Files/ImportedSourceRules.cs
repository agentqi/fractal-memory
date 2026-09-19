namespace FractalMemory.Core.Infrastructure.Files;

internal static class ImportedSourceRules
{
    public static bool IsSupported(string path) => Path.GetExtension(path).ToLowerInvariant() is ".md" or ".html" or ".htm" or ".txt";
    public static string ArtifactPath(string sourceName)
    {
        if (!IsSupported(sourceName)) throw new ArgumentException("Import supports .md, .html, .htm and .txt notes.");
        return "artifacts/source" + Path.GetExtension(sourceName).ToLowerInvariant();
    }
    public static bool IsSourceArtifact(string path) => IsSupported(path) && path == ArtifactPath(path);
}
