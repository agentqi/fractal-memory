using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Rules;

namespace FractalMemory.McpServer;

public interface IMcpRepositoryContext
{
    string GetRepositoryRoot();
    string GetServiceWorkingDirectory();
    string GetStoragePath(string relativePath);
    string GetNodeFilePath(string encodedNodePath, string fileName);
}

public sealed class McpRepositoryContext(IRepositoryService repositoryService) : IMcpRepositoryContext
{
    private const string RepositoryRootEnvironmentVariable = "FRACTALMEM_REPOSITORY_ROOT";

    public string GetRepositoryRoot()
    {
        var startDirectory = GetStartDirectory();
        var configuredStorageRoot = Path.Combine(startDirectory, "config.yaml");
        if (Path.GetFileName(startDirectory).Equals(".fractal-memory", StringComparison.Ordinal) &&
            File.Exists(configuredStorageRoot))
        {
            return Directory.GetParent(startDirectory)?.FullName
                ?? throw new InvalidOperationException("The configured FractalMem storage root must have a parent directory.");
        }

        if (File.Exists(Path.Combine(startDirectory, ".fractal-memory", "config.yaml")))
        {
            return startDirectory;
        }

        return repositoryService.FindRepositoryRoot(startDirectory)
            ?? throw new InvalidOperationException(
                $"No FractalMemory repository found. Set {RepositoryRootEnvironmentVariable} to the repository root or launch the MCP server from a workspace inside the repository.");
    }

    public string GetServiceWorkingDirectory() => GetRepositoryRoot();

    public string GetStoragePath(string relativePath)
    {
        var storageRoot = repositoryService.GetStorageRoot(GetRepositoryRoot());
        var candidate = Path.GetFullPath(Path.Combine(storageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        EnsureContained(storageRoot, candidate);
        return candidate;
    }

    public string GetNodeFilePath(string encodedNodePath, string fileName)
    {
        var normalizedPath = NodePathRules.Normalize(Uri.UnescapeDataString(encodedNodePath));
        return GetStoragePath($"{normalizedPath}/{fileName}");
    }

    private static string GetStartDirectory()
    {
        var configured = Environment.GetEnvironmentVariable(RepositoryRootEnvironmentVariable);
        var startDirectory = string.IsNullOrWhiteSpace(configured)
            ? Environment.CurrentDirectory
            : configured.Trim();
        return Path.GetFullPath(startDirectory);
    }

    private static void EnsureContained(string rootPath, string candidatePath)
    {
        var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var requiredPrefix = normalizedRoot + Path.DirectorySeparatorChar;
        if (!candidatePath.Equals(normalizedRoot, comparison) &&
            !candidatePath.StartsWith(requiredPrefix, comparison))
        {
            throw new InvalidOperationException("Requested path resolves outside the FractalMem repository.");
        }
    }
}
