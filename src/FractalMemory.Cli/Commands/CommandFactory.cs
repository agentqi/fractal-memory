using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Application.UseCases;
using FractalMemory.Core.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using System.CommandLine;
using FractalMemory.Cli;

namespace FractalMemory.Cli.Commands;

public static class CommandFactory
{
    public static RootCommand Create(ServiceProvider provider)
    {
        var formatter = provider.GetRequiredService<IHumanFormatter>();
        var aiFormatter = provider.GetRequiredService<IAiExportFormatter>();

        var root = new RootCommand("FractalMem: local-first structured memory for AI-assisted work.");

        var init = new Command("init", "Initialize a FractalMem repository in the current directory.");
        init.SetAction((_, cancellationToken) => ExecuteAsync(async () =>
        {
            var useCase = provider.GetRequiredService<InitRepositoryUseCase>();
            var path = await useCase.ExecuteAsync(Environment.CurrentDirectory, cancellationToken);
            Console.WriteLine(formatter.FormatInitialization(Path.GetDirectoryName(path)!));
        }));

        var node = new Command("node", "Node operations.");
        var create = new Command("create", "Create a node.");
        var nodePathArgument = new Argument<string>("path");
        var nodeFormatOption = new Option<NodeFileFormat>("--format") { DefaultValueFactory = _ => NodeFileFormat.Markdown };
        create.Arguments.Add(nodePathArgument);
        create.Options.Add(nodeFormatOption);
        create.SetAction((parseResult, cancellationToken) => ExecuteAsync(async () =>
        {
            var useCase = provider.GetRequiredService<CreateNodeUseCase>();
            var result = await useCase.ExecuteAsync(
                Environment.CurrentDirectory,
                parseResult.GetRequiredValue(nodePathArgument),
                parseResult.GetValue(nodeFormatOption),
                cancellationToken);
            Console.WriteLine(formatter.FormatNodeCreated(result));
        }));
        node.Subcommands.Add(create);

        var open = new Command("open", "Open a node.");
        var openPathArgument = new Argument<string>("path");
        var depthOption = new Option<RetrievalDepth?>("--depth");
        var viewOption = new Option<NodeViewType>("--view") { DefaultValueFactory = _ => NodeViewType.Index };
        open.Arguments.Add(openPathArgument);
        open.Options.Add(depthOption);
        open.Options.Add(viewOption);
        open.SetAction((parseResult, cancellationToken) => ExecuteAsync(async () =>
        {
            var useCase = provider.GetRequiredService<OpenNodeUseCase>();
            var config = await LoadConfigAsync(provider, cancellationToken);
            var result = await useCase.ExecuteAsync(
                Environment.CurrentDirectory,
                parseResult.GetRequiredValue(openPathArgument),
                parseResult.GetValue(depthOption) ?? config.DefaultDepth,
                parseResult.GetValue(viewOption),
                cancellationToken);
            Console.WriteLine(formatter.FormatOpen(result));
        }));

        var search = new Command("search", "Search memory.");
        var queryArgument = new Argument<string>("query");
        var searchLimitOption = new Option<int?>("--limit");
        var searchScopeOption = new Option<string?>("--scope");
        search.Arguments.Add(queryArgument);
        search.Options.Add(searchLimitOption);
        search.Options.Add(searchScopeOption);
        search.SetAction((parseResult, cancellationToken) => ExecuteAsync(async () =>
        {
            var useCase = provider.GetRequiredService<SearchUseCase>();
            var query = parseResult.GetRequiredValue(queryArgument);
            var results = await useCase.ExecuteAsync(
                Environment.CurrentDirectory,
                query,
                cancellationToken,
                parseResult.GetValue(searchLimitOption),
                parseResult.GetValue(searchScopeOption));
            Console.WriteLine(formatter.FormatSearch(results, query));
        }));

        var export = new Command("export", "Export a node as AI-friendly text.");
        var exportPathArgument = new Argument<string>("path");
        var modeOption = new Option<ExportMode?>("--mode");
        export.Arguments.Add(exportPathArgument);
        export.Options.Add(modeOption);
        export.SetAction((parseResult, cancellationToken) => ExecuteAsync(async () =>
        {
            var useCase = provider.GetRequiredService<ExportUseCase>();
            var config = await LoadConfigAsync(provider, cancellationToken);
            var document = await useCase.ExecuteAsync(
                Environment.CurrentDirectory,
                parseResult.GetRequiredValue(exportPathArgument),
                parseResult.GetValue(modeOption) ?? config.DefaultExportMode,
                cancellationToken);
            Console.WriteLine(aiFormatter.Format(document));
        }));

        var handoff = new Command("handoff", "Handoff operations.");
        var handoffCreate = new Command("create", "Create a handoff file.");
        var handoffPathArgument = new Argument<string>("path");
        handoffCreate.Arguments.Add(handoffPathArgument);
        handoffCreate.SetAction((parseResult, cancellationToken) => ExecuteAsync(async () =>
        {
            var useCase = provider.GetRequiredService<CreateHandoffUseCase>();
            var packet = await useCase.ExecuteAsync(Environment.CurrentDirectory, parseResult.GetRequiredValue(handoffPathArgument), cancellationToken);
            Console.WriteLine(formatter.FormatHandoff(packet));
        }));
        handoff.Subcommands.Add(handoffCreate);

        var recent = new Command("recent", "Show recent changes.");
        var limitOption = new Option<int>("--limit") { DefaultValueFactory = _ => 10 };
        var daysOption = new Option<int>("--days") { DefaultValueFactory = _ => 30 };
        var scopeOption = new Option<string?>("--scope");
        recent.Options.Add(limitOption);
        recent.Options.Add(daysOption);
        recent.Options.Add(scopeOption);
        recent.SetAction((parseResult, cancellationToken) => ExecuteAsync(async () =>
        {
            var useCase = provider.GetRequiredService<GetRecentUseCase>();
            var items = await useCase.ExecuteAsync(
                Environment.CurrentDirectory,
                parseResult.GetValue(limitOption),
                parseResult.GetValue(daysOption),
                parseResult.GetValue(scopeOption),
                cancellationToken);
            Console.WriteLine(formatter.FormatRecent(items));
        }));

        var index = new Command("index", "Index operations.");
        var refresh = new Command("refresh", "Refresh repository indexes.");
        refresh.SetAction((_, cancellationToken) => ExecuteAsync(async () =>
        {
            var useCase = provider.GetRequiredService<RefreshIndexesUseCase>();
            await useCase.ExecuteAsync(Environment.CurrentDirectory, cancellationToken);
            Console.WriteLine(formatter.FormatIndexesRefreshed());
        }));
        index.Subcommands.Add(refresh);

        var validate = new Command("validate", "Validate the repository.");
        validate.SetAction((_, cancellationToken) => ExecuteWithExitCodeAsync(async () =>
        {
            var useCase = provider.GetRequiredService<ValidateRepositoryUseCase>();
            var report = await useCase.ExecuteAsync(Environment.CurrentDirectory, cancellationToken);
            Console.WriteLine(formatter.FormatValidation(report));
            return report.HasErrors ? CliExitCodes.ValidationFailed : CliExitCodes.Success;
        }));

        root.Subcommands.Add(init);
        root.Subcommands.Add(node);
        root.Subcommands.Add(open);
        root.Subcommands.Add(search);
        root.Subcommands.Add(export);
        root.Subcommands.Add(handoff);
        root.Subcommands.Add(recent);
        root.Subcommands.Add(index);
        root.Subcommands.Add(validate);
        return root;

        static async Task<int> ExecuteAsync(Func<Task> action)
        {
            try
            {
                await action();
                return CliExitCodes.Success;
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
        }

        static async Task<int> ExecuteWithExitCodeAsync(Func<Task<int>> action)
        {
            try
            {
                return await action();
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
        }

        static async Task<FractalMemory.Core.Domain.Models.RepositoryConfig> LoadConfigAsync(
            ServiceProvider provider,
            CancellationToken cancellationToken)
        {
            var repositoryService = provider.GetRequiredService<IRepositoryService>();
            var repositoryRoot = repositoryService.FindRepositoryRoot(Environment.CurrentDirectory)
                ?? throw new InvalidOperationException("No FractalMemory repository found.");
            return await repositoryService.LoadConfigAsync(repositoryRoot, cancellationToken);
        }
    }
}
