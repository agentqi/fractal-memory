using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Models;
using FractalMemory.Core.Infrastructure.Parsing;
using Microsoft.Extensions.DependencyInjection;

namespace FractalMemory.Core.Tests;

public sealed class ParsingAndValidationTests
{
    [Fact]
    public void FrontMatterParserReadsFields()
    {
        var parser = new YamlFrontMatterParser();
        var markdown = """
            ---
            title: Sample Node
            aliases: [sample, alpha]
            tags: [one, two]
            status: paused
            priority: critical
            last_updated: 2026-04-02T12:00:00Z
            owner: tester
            summary: Parser smoke test
            ---

            # Body

            Useful content.
            """;

        var parsed = parser.Parse(markdown);

        Assert.True(parsed.HasFrontMatter);
        Assert.Equal("Sample Node", parsed.Metadata.Title);
        Assert.Contains("alpha", parsed.Metadata.Aliases);
        Assert.Contains("two", parsed.Metadata.Tags);
        Assert.Equal("Parser smoke test", parsed.Metadata.Summary);
        Assert.Equal("Useful content.", parsed.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last());
    }

    [Fact]
    public async Task ValidationReportsHardErrorsAndWarnings()
    {
        using var provider = TestEnvironment.CreateServices();
        var repositoryService = provider.GetRequiredService<IRepositoryService>();
        var validationService = provider.GetRequiredService<IValidationService>();
        var temp = TestEnvironment.CreateTempDirectory();

        await repositoryService.InitializeAsync(temp, CancellationToken.None);
        var brokenNode = Path.Combine(temp, ".fractal-memory", "projects", "invalid node");
        Directory.CreateDirectory(brokenNode);
        await File.WriteAllTextAsync(Path.Combine(brokenNode, "index.md"), "---\ninvalid: [\n---\n");

        var report = await validationService.ValidateAsync(temp, CancellationToken.None);

        Assert.Contains(report.Issues, issue => issue.Severity == ValidationSeverity.Error);
        Assert.Contains(report.Issues, issue => issue.Message.Contains("Indexes may be stale", StringComparison.Ordinal));
    }
}
