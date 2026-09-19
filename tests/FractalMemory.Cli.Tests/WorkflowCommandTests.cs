using System.Text.Json;
using FractalMemory.Cli;
using FractalMemory.Core.Application;
using Microsoft.Extensions.DependencyInjection;

namespace FractalMemory.Cli.Tests;

public sealed class WorkflowCommandTests
{
    [Fact]
    public async Task JsonReadUpdateAppendResumeAndErrorsRoundTripThroughCli()
    {
        using var provider = new ServiceCollection().AddFractalMemoryCore().BuildServiceProvider();
        var root = Path.Combine(Path.GetTempPath(), "fractalmem-cli-workflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var oldDirectory = Environment.CurrentDirectory;
        var oldOut = Console.Out; var oldError = Console.Error;
        using var output = new StringWriter(); using var error = new StringWriter();
        try
        {
            Environment.CurrentDirectory = root;
            Console.SetOut(output); Console.SetError(error);
            async Task<JsonElement> Run(params string[] arguments)
            {
                output.GetStringBuilder().Clear(); error.GetStringBuilder().Clear();
                Assert.Equal(0, await CliRunner.RunAsync([.. arguments, "--json"], provider));
                return JsonDocument.Parse(output.ToString()).RootElement.Clone();
            }
            var initialized = await Run("init");
            Assert.Equal(Environment.CurrentDirectory, initialized.GetProperty("repositoryRoot").GetString());
            await Run("node", "create", "projects/cli");
            var document = await Run("read", "projects/cli");
            var hash = document.GetProperty("hash").GetString()!;
            var written = await Run("update", "projects/cli", "Current Objective", "Verify the CLI workflow.", "--expected-hash", hash);
            Assert.Contains("Verify the CLI workflow.", written.GetProperty("document").GetProperty("content").GetString(), StringComparison.Ordinal);
            output.GetStringBuilder().Clear();
            Assert.Equal(2, await CliRunner.RunAsync(["update", "projects/cli", "Current Objective", "Stale overwrite", "--expected-hash", hash, "--json"], provider));
            Assert.Empty(output.ToString());
            Assert.Contains("changed since", JsonDocument.Parse(error.ToString()).RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
            var timeline = await Run("read", "projects/cli", "--file", "timeline");
            await Run("append", "projects/cli", "timeline", "Verified current state.", "--expected-hash", timeline.GetProperty("hash").GetString()!);
            await Run("handoff", "create", "projects/cli");
            var resume = await Run("resume", "projects/cli", "--max-characters", "600");
            Assert.True(resume.GetProperty("comparisonAvailable").GetBoolean());
            Assert.InRange(resume.GetProperty("context").GetProperty("usedCharacters").GetInt32(), 1, 600);
            Assert.Single((await Run("list", "--scope", "projects")).EnumerateArray());
            Assert.True((await Run("doctor", "--repair")).GetProperty("indexesRebuilt").GetBoolean());
            var sourceFile = Path.Combine(root, "note.txt");
            await File.WriteAllTextAsync(sourceFile, "A source note.", TestContext.Current.CancellationToken);
            foreach (var (arguments, expected) in new (string[], string)[]
            {
                (["doctor"], "Use 'fm doctor --repair'"),
                (["import", sourceFile, "projects/import"], "Ready to import:"),
                (["decisions", "projects/cli"], "No managed decisions found."),
                (["attention", "--scope", "projects/cli"], "Missing Key Prior Decision."),
                (["handoff", "list", "projects/cli"], "source snapshot available"),
            })
            {
                output.GetStringBuilder().Clear();
                Assert.Equal(0, await CliRunner.RunAsync(arguments, provider));
                Assert.Contains(expected, output.ToString(), StringComparison.Ordinal);
                Assert.DoesNotContain("\"nodePath\"", output.ToString(), StringComparison.Ordinal);
            }
            error.GetStringBuilder().Clear();
            Assert.Equal(2, await CliRunner.RunAsync(["context", "projects/cli", "--max-characters", "invalid", "--json"], provider));
            Assert.NotEmpty(JsonDocument.Parse(error.ToString()).RootElement.GetProperty("error").GetString()!);
        }
        finally
        {
            Environment.CurrentDirectory = oldDirectory;
            Console.SetOut(oldOut); Console.SetError(oldError);
            Directory.Delete(root, recursive: true);
        }
    }
}
