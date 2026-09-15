using FractalMemory.Core.Application.Services;

namespace FractalMemory.Core.Infrastructure.Files;

public sealed class LocalFileSystemService : IFileSystemService
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void MoveDirectory(string sourcePath, string destinationPath) => Directory.Move(sourcePath, destinationPath);

    public void DeleteDirectory(string path) => Directory.Delete(path, recursive: true);

    public async Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Cannot determine the parent directory for '{path}'.");
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllTextAsync(path, cancellationToken);

    public IEnumerable<string> EnumerateDirectories(string path) =>
        Directory.Exists(path)
            ? Directory.EnumerateDirectories(path, "*", CreateEnumerationOptions(recurse: false))
            : [];

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) =>
        Directory.Exists(path)
            ? Directory.EnumerateFiles(path, searchPattern, CreateEnumerationOptions(searchOption == SearchOption.AllDirectories))
            : [];

    public DateTimeOffset GetLastWriteTimeUtc(string path)
    {
        if (Directory.Exists(path))
        {
            return Directory.GetLastWriteTimeUtc(path);
        }

        return File.GetLastWriteTimeUtc(path);
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static EnumerationOptions CreateEnumerationOptions(bool recurse) => new()
    {
        RecurseSubdirectories = recurse,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
    };
}
