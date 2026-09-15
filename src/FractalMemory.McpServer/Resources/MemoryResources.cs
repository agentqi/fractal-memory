using System.ComponentModel;
using FractalMemory.Core.Application.Services;
using FractalMemory.McpServer;
using ModelContextProtocol.Server;

namespace FractalMemory.McpServer.Resources;

[McpServerResourceType]
public sealed class MemoryResources(
    IFileSystemService fileSystemService,
    IRepositoryService repositoryService,
    IMcpRepositoryContext repositoryContext)
{
    [McpServerResource(UriTemplate = "memory://root/index", Name = "Root Index", MimeType = "text/plain")]
    [Description("The root memory index entry point in Markdown or HTML format.")]
    public async Task<string> RootIndex(CancellationToken cancellationToken = default)
    {
        var path = repositoryContext.GetNodeFilePath("root", "index.md");
        return await fileSystemService.ReadAllTextAsync(path, cancellationToken);
    }

    [McpServerResource(UriTemplate = "memory://node/index/{path}", Name = "Node Index", MimeType = "text/plain")]
    [Description("A node index resource in Markdown or HTML format. Pass the repository-relative path URL-encoded in the path parameter.")]
    public async Task<string> NodeIndex([Description("URL-encoded repository-relative node path.")] string path, CancellationToken cancellationToken = default)
    {
        return await fileSystemService.ReadAllTextAsync(repositoryContext.GetNodeFilePath(path, "index.md"), cancellationToken);
    }

    [McpServerResource(UriTemplate = "memory://node/state/{path}", Name = "Node State", MimeType = "text/plain")]
    [Description("A node state resource in Markdown or HTML format. Pass the repository-relative path URL-encoded in the path parameter.")]
    public async Task<string> NodeState([Description("URL-encoded repository-relative node path.")] string path, CancellationToken cancellationToken = default)
    {
        return await fileSystemService.ReadAllTextAsync(repositoryContext.GetNodeFilePath(path, "state.md"), cancellationToken);
    }

    [McpServerResource(UriTemplate = "memory://handoffs/latest", Name = "Latest Handoff", MimeType = "text/markdown")]
    [Description("The latest generated handoff markdown file.")]
    public async Task<string> LatestHandoff(CancellationToken cancellationToken = default)
    {
        var repositoryRoot = repositoryContext.GetRepositoryRoot();
        var config = await repositoryService.LoadConfigAsync(repositoryRoot, cancellationToken);
        var handoffsRoot = repositoryContext.GetStoragePath(config.Handoffs.Directory);
        var latest = fileSystemService.EnumerateFiles(handoffsRoot, "*.md", SearchOption.TopDirectoryOnly)
            .OrderByDescending(fileSystemService.GetLastWriteTimeUtc)
            .FirstOrDefault();

        return latest is null
            ? "# No handoffs yet"
            : await fileSystemService.ReadAllTextAsync(latest, cancellationToken);
    }
}
