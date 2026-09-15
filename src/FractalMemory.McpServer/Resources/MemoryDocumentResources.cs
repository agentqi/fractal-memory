using System.ComponentModel;
using FractalMemory.Core.Application.Services;
using ModelContextProtocol.Server;

namespace FractalMemory.McpServer.Resources;

[McpServerResourceType]
public sealed class MemoryDocumentResources(IMemoryWorkflowService workflow, IMcpRepositoryContext context)
{
    [McpServerResource(UriTemplate = "memory://document/{path}/{file}", Name = "Memory Source", MimeType = "text/plain")]
    [Description("Full source document. URL-encode the node path and filename independently, including slashes in artifacts/ paths.")]
    public async Task<string> Read(string path, string file, CancellationToken cancellationToken = default) =>
        (await Tools.MemoryWorkflowTools.Expected(() => workflow.ReadAsync(context.GetServiceWorkingDirectory(),
            Uri.UnescapeDataString(path), Uri.UnescapeDataString(file), null, cancellationToken))).Content;
}
