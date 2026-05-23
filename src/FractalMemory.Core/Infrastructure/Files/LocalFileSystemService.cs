using FractalMemory.Core.Application.Services;

namespace FractalMemory.Core.Infrastructure.Files;

public sealed class LocalFileSystemService : IFileSystemService
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, content, cancellationToken);

    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllTextAsync(path, cancellationToken);

    public IEnumerable<string> EnumerateDirectories(string path) =>
        Directory.Exists(path) ? Directory.EnumerateDirectories(path) : [];

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) =>
        Directory.Exists(path) ? Directory.EnumerateFiles(path, searchPattern, searchOption) : [];

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
}
