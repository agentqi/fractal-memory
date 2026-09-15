using FractalMemory.Cli.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace FractalMemory.Cli;

public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args, ServiceProvider provider)
    {
        try
        {
            var root = CommandFactory.Create(provider);
            var parsed = root.Parse(args);
            if (parsed.Errors.Count > 0 && args.Contains("--json", StringComparer.Ordinal))
            {
                await Console.Error.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(new { error = string.Join(" ", parsed.Errors.Select(e => e.Message)) }, WorkflowCommands.JsonOptions));
                return CliExitCodes.UserError;
            }
            return await parsed.InvokeAsync();
        }
        catch (ArgumentException exception)
        {
            await Console.Error.WriteLineAsync($"Error: {exception.Message}");
            return CliExitCodes.UserError;
        }
        catch (InvalidOperationException exception)
        {
            await Console.Error.WriteLineAsync($"Error: {exception.Message}");
            return CliExitCodes.UserError;
        }
        catch (IOException exception)
        {
            await Console.Error.WriteLineAsync($"Error: {exception.Message}");
            return CliExitCodes.UserError;
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync($"Unexpected error: {exception.Message}");
            return CliExitCodes.UnexpectedError;
        }
    }
}

public static class CliExitCodes
{
    public const int Success = 0;
    public const int ValidationFailed = 1;
    public const int UserError = 2;
    public const int UnexpectedError = 3;
}
