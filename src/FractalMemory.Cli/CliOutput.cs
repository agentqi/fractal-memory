using System.Text.Json;
using FractalMemory.Cli.Commands;

namespace FractalMemory.Cli;

internal static class CliOutput
{
    public static bool IsUserError(Exception exception) => exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException;
    public static void WriteError(bool json, string message) => Console.Error.WriteLine(json
        ? JsonSerializer.Serialize(new { error = message }, WorkflowCommands.JsonOptions)
        : $"Error: {message}");
}
