using FractalMemory.Cli;
using FractalMemory.Core.Application;
using Microsoft.Extensions.DependencyInjection;

namespace FractalMemory.Cli.Tests;

public sealed class ConfigurationCommandTests
{
    [Theory]
    [InlineData("open", "root", "--depth", "999")]
    [InlineData("open", "root", "--depth", "-1")]
    [InlineData("open", "root", "--view", "999")]
    [InlineData("export", "root", "--mode", "999")]
    [InlineData("node", "create", "projects/invalid", "--format", "999")]
    public async Task UndefinedEnumArgumentsReturnUserErrors(params string[] arguments)
    {
        using var provider = CreateProvider();
        var temp = CreateTempDirectory();
        var originalDirectory = Environment.CurrentDirectory;
        var originalError = Console.Error;
        using var errors = new StringWriter();
        try
        {
            Environment.CurrentDirectory = temp;
            Console.SetError(errors);
            Assert.Equal(0, await CliRunner.RunAsync(["init"], provider));
            Assert.Equal(CliExitCodes.UserError, await CliRunner.RunAsync(arguments, provider));
            Assert.Contains("Error:", errors.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
            Environment.CurrentDirectory = originalDirectory;
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task OpenAndExportHonorConfiguredDefaults()
    {
        using var provider = CreateProvider();
        var temp = CreateTempDirectory();
        var originalDirectory = Environment.CurrentDirectory;
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Environment.CurrentDirectory = temp;
            Console.SetOut(writer);
            Assert.Equal(0, await CliRunner.RunAsync(["init"], provider));
            Assert.Equal(0, await CliRunner.RunAsync(["node", "create", "projects/configured"], provider));
            await ReplaceConfigValue(temp, "default_depth: 1", "default_depth: 3");
            await ReplaceConfigValue(temp, "default_export_mode: compact", "default_export_mode: verbose");

            writer.GetStringBuilder().Clear();
            Assert.Equal(0, await CliRunner.RunAsync(["open", "projects/configured"], provider));
            var opened = writer.ToString();
            Assert.Contains("State", opened, StringComparison.Ordinal);
            Assert.Contains("Recent Timeline", opened, StringComparison.Ordinal);

            writer.GetStringBuilder().Clear();
            Assert.Equal(0, await CliRunner.RunAsync(["export", "projects/configured"], provider));
            var exported = writer.ToString();
            Assert.Contains("mode: verbose", exported, StringComparison.Ordinal);
            Assert.Contains("timeline_highlights:", exported, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.CurrentDirectory = originalDirectory;
        }
    }

    [Fact]
    public async Task FreshCliRepositoryValidatesWithoutWarnings()
    {
        using var provider = CreateProvider();
        var temp = CreateTempDirectory();
        var originalDirectory = Environment.CurrentDirectory;
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Environment.CurrentDirectory = temp;
            Console.SetOut(writer);
            Assert.Equal(0, await CliRunner.RunAsync(["init"], provider));
            Assert.Equal(0, await CliRunner.RunAsync(["node", "create", "projects/clean"], provider));

            writer.GetStringBuilder().Clear();
            Assert.Equal(0, await CliRunner.RunAsync(["validate"], provider));
            Assert.Equal("Validation passed with no issues.", writer.ToString().Trim());
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.CurrentDirectory = originalDirectory;
        }
    }

    [Fact]
    public async Task InvalidSearchLimitReturnsUserError()
    {
        using var provider = CreateProvider();
        var temp = CreateTempDirectory();
        var originalDirectory = Environment.CurrentDirectory;
        var originalError = Console.Error;
        using var writer = new StringWriter();

        try
        {
            Environment.CurrentDirectory = temp;
            Console.SetError(writer);
            Assert.Equal(0, await CliRunner.RunAsync(["init"], provider));

            var exitCode = await CliRunner.RunAsync(["search", "anything", "--limit", "0"], provider);

            Assert.Equal(CliExitCodes.UserError, exitCode);
            Assert.Contains("positive integer", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetError(originalError);
            Environment.CurrentDirectory = originalDirectory;
        }
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddFractalMemoryCore();
        return services.BuildServiceProvider();
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "fractalmem-cli-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task ReplaceConfigValue(string repositoryRoot, string oldValue, string newValue)
    {
        var configPath = Path.Combine(repositoryRoot, ".fractal-memory", "config.yaml");
        var config = await TestFile.ReadAllTextAsync(configPath);
        Assert.Contains(oldValue, config, StringComparison.Ordinal);
        await TestFile.WriteAllTextAsync(configPath, config.Replace(oldValue, newValue, StringComparison.Ordinal));
    }
}
