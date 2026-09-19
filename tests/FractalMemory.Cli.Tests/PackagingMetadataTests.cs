using System.Text.Json;

namespace FractalMemory.Cli.Tests;

public sealed class PackagingMetadataTests
{
    [Fact]
    public void PluginAndMarketplaceMetadataStayAligned()
    {
        var root = FindRepositoryRoot();
        using var codexPlugin = ReadJson(root, "plugins/codex-fractalmem/.codex-plugin/plugin.json");
        using var claudePlugin = ReadJson(root, "plugins/claude-fractalmem/.claude-plugin/plugin.json");
        using var codexMarketplace = ReadJson(root, ".agents/plugins/marketplace.json");
        using var claudeMarketplace = ReadJson(root, ".claude-plugin/marketplace.json");

        var codexRoot = codexPlugin.RootElement;
        var claudeRoot = claudePlugin.RootElement;
        var version = codexRoot.GetProperty("version").GetString();

        Assert.Equal("codex-fractalmem", codexRoot.GetProperty("name").GetString());
        Assert.Equal("fractalmem", claudeRoot.GetProperty("name").GetString());
        Assert.Equal(version, claudeRoot.GetProperty("version").GetString());
        Assert.Equal(
            "codex-fractalmem",
            codexMarketplace.RootElement.GetProperty("plugins")[0].GetProperty("name").GetString());
        Assert.Equal(
            "fractalmem",
            claudeMarketplace.RootElement.GetProperty("plugins")[0].GetProperty("name").GetString());
        Assert.Equal(
            version,
            claudeMarketplace.RootElement.GetProperty("plugins")[0].GetProperty("version").GetString());

        var buildProperties = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        Assert.Contains($"<VersionPrefix>{version}</VersionPrefix>", buildProperties, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseAutomationAndPackageSmokeAssetsExist()
    {
        var root = FindRepositoryRoot();
        foreach (var relativePath in new[]
        {
            ".github/workflows/ci.yml",
            ".github/workflows/codeql.yml",
            ".github/workflows/release.yml",
            ".github/dependabot.yml",
            "scripts/smoke-packages.sh",
            "docs/releasing.md",
            "LICENSE",
            "NOTICE",
        })
        {
            Assert.True(File.Exists(Path.Combine(root, relativePath)), $"Missing release asset: {relativePath}");
        }
    }

    private static JsonDocument ReadJson(string root, string relativePath) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(root, relativePath)));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "FractalMemory.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the FractalMem repository root.");
    }
}
