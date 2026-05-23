using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Models;
using FractalMemory.Core.Infrastructure.Parsing;

namespace FractalMemory.Core.Infrastructure.Files;

public sealed class MarkdownFileService(IFileSystemService fileSystemService, IFrontMatterParser parser) : IMarkdownFileService
{
    public async Task<ParsedMarkdownDocument> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var content = await fileSystemService.ReadAllTextAsync(path, cancellationToken);
        return parser.Parse(content);
    }

    public Task WriteAsync(string path, NodeMetadata metadata, string content, CancellationToken cancellationToken)
    {
        var markdown = $"{YamlFrontMatterParser.Render(metadata)}{Environment.NewLine}{Environment.NewLine}{content.TrimEnd()}{Environment.NewLine}";
        return fileSystemService.WriteAllTextAsync(path, markdown, cancellationToken);
    }
}
