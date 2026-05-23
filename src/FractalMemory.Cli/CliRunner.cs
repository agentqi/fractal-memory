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
            return await root.Parse(args).InvokeAsync();
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
