using FractalMemory.Cli;
using FractalMemory.Core.Application;
using Microsoft.Extensions.DependencyInjection;

namespace FractalMemory.Cli.Tests;

public sealed class CliCommandTests
{
    [Fact]
    public async Task InitCommandCreatesRepository()
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

            var exitCode = await CliRunner.RunAsync(["init"], provider);

            Assert.Equal(0, exitCode);
            Assert.Contains("Initialized FractalMem repository", writer.ToString(), StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(temp, ".fractal-memory", "config.yaml")));
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.CurrentDirectory = originalDirectory;
        }
    }

    [Fact]
    public async Task ValidateCommandReturnsNonZeroForBrokenRepository()
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
            await CliRunner.RunAsync(["init"], provider);

            File.Delete(Path.Combine(temp, ".fractal-memory", "root", "state.md"));
            writer.GetStringBuilder().Clear();

            var exitCode = await CliRunner.RunAsync(["validate"], provider);

            Assert.Equal(1, exitCode);
            Assert.Contains("Missing required state.md", writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.CurrentDirectory = originalDirectory;
        }
    }

    [Fact]
    public async Task RunnerMapsExpectedErrorsToCleanMessages()
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

            var exitCode = await CliRunner.RunAsync(["open", "projects/missing"], provider);

            Assert.Equal(CliExitCodes.UserError, exitCode);
            Assert.Contains("No FractalMemory repository found.", writer.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("at FractalMemory", writer.ToString(), StringComparison.Ordinal);
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
        var path = Path.Combine(Path.GetTempPath(), "fractalmem-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
