using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Application.UseCases;
using FractalMemory.Core.Infrastructure.Files;
using FractalMemory.Core.Infrastructure.Indexing;
using FractalMemory.Core.Infrastructure.Parsing;
using FractalMemory.Core.Infrastructure.Search;
using FractalMemory.Core.Infrastructure.Time;
using FractalMemory.Core.Output;
using Microsoft.Extensions.DependencyInjection;

namespace FractalMemory.Core.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFractalMemoryCore(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IFileSystemService, LocalFileSystemService>();
        services.AddSingleton<IFrontMatterParser, YamlFrontMatterParser>();
        services.AddSingleton<IStructuredMemoryService, StructuredMemoryService>();
        services.AddSingleton<IMarkdownFileService, MarkdownFileService>();
        services.AddSingleton<ITemplateService, TemplateService>();
        services.AddSingleton<IRepositoryService, RepositoryService>();
        services.AddSingleton<INodeListingCacheReader, NodeListingCacheReader>();
        services.AddSingleton<INodeService, NodeService>();
        services.AddSingleton<IReadService, ReadService>();
        services.AddSingleton<IIndexService, IndexService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IHandoffService, HandoffService>();
        services.AddSingleton<IValidationService, ValidationService>();
        services.AddSingleton<IMemoryWorkflowService, MemoryWorkflowService>();
        services.AddSingleton<IHumanFormatter, HumanFormatter>();
        services.AddSingleton<IAiExportFormatter, AiExportFormatter>();
        services.AddSingleton<InitRepositoryUseCase>();
        services.AddSingleton<CreateNodeUseCase>();
        services.AddSingleton<OpenNodeUseCase>();
        services.AddSingleton<SearchUseCase>();
        services.AddSingleton<ExportUseCase>();
        services.AddSingleton<CreateHandoffUseCase>();
        services.AddSingleton<GetRecentUseCase>();
        services.AddSingleton<RefreshIndexesUseCase>();
        services.AddSingleton<ValidateRepositoryUseCase>();
        return services;
    }
}
