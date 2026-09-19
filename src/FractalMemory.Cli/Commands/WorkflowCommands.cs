using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using FractalMemory.Core.Domain.Models;

namespace FractalMemory.Cli.Commands;

internal static class WorkflowCommands
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void Add(RootCommand root, Command handoff, IMemoryWorkflowService service, Option<bool> json, IHumanFormatter formatter)
    {
        var read = new Command("read", "Read a complete source or named section, including its hash for safe updates.");
        var readPath = new Argument<string>("path");
        var readFile = new Option<string>("--file") { DefaultValueFactory = _ => "state" };
        var section = new Option<string?>("--section");
        read.Arguments.Add(readPath); read.Options.Add(readFile); read.Options.Add(section);
        read.SetAction((p, ct) => Run(p, json, formatter, () => service.ReadAsync(Environment.CurrentDirectory, p.GetRequiredValue(readPath), p.GetRequiredValue(readFile), p.GetValue(section), ct)));
        root.Subcommands.Add(read);

        var update = new Command("update", "Replace an existing Markdown section, rejecting stale document hashes.");
        var path = new Argument<string>("path"); var heading = new Argument<string>("section"); var content = new Argument<string>("content");
        var file = new Option<string>("--file") { DefaultValueFactory = _ => "state" };
        var hash = new Option<string>("--expected-hash") { Required = true };
        update.Arguments.Add(path); update.Arguments.Add(heading); update.Arguments.Add(content); update.Options.Add(file); update.Options.Add(hash);
        update.SetAction((p, ct) => Run(p, json, formatter, () => service.SetSectionAsync(Environment.CurrentDirectory, p.GetRequiredValue(path), p.GetRequiredValue(file), p.GetRequiredValue(heading), p.GetRequiredValue(content), p.GetRequiredValue(hash), ct)));
        root.Subcommands.Add(update);

        var append = new Command("append", "Append a dated timeline entry or an active decision.");
        var appendPath = new Argument<string>("path"); var appendFile = new Argument<string>("file"); var appendContent = new Argument<string>("content");
        var appendHash = new Option<string>("--expected-hash") { Required = true }; var supersedes = new Option<string?>("--supersedes");
        append.Arguments.Add(appendPath); append.Arguments.Add(appendFile); append.Arguments.Add(appendContent); append.Options.Add(appendHash); append.Options.Add(supersedes);
        append.SetAction((p, ct) => Run(p, json, formatter, () => service.AppendAsync(Environment.CurrentDirectory, p.GetRequiredValue(appendPath), p.GetRequiredValue(appendFile), p.GetRequiredValue(appendContent), p.GetRequiredValue(appendHash), p.GetValue(supersedes), ct)));
        root.Subcommands.Add(append);

        var decisions = new Command("decisions", "List managed decision IDs and content; use --all to include superseded decisions.");
        var decisionPath = new Argument<string>("path"); var allDecisions = new Option<bool>("--all");
        decisions.Arguments.Add(decisionPath); decisions.Options.Add(allDecisions);
        decisions.SetAction((p, ct) => Run(p, json, formatter, () => service.DecisionsAsync(Environment.CurrentDirectory, p.GetRequiredValue(decisionPath), p.GetValue(allDecisions), ct)));
        root.Subcommands.Add(decisions);

        var review = new Command("review", "Record a review date and optional status on the node index.");
        var reviewPath = new Argument<string>("path"); var reviewHash = new Option<string>("--expected-hash") { Required = true };
        var reviewAfter = new Option<DateTimeOffset>("--after") { Required = true }; var status = new Option<NodeStatus?>("--status");
        review.Arguments.Add(reviewPath); review.Options.Add(reviewHash); review.Options.Add(reviewAfter); review.Options.Add(status);
        review.SetAction((p, ct) => Run(p, json, formatter, () => service.ReviewAsync(Environment.CurrentDirectory, p.GetRequiredValue(reviewPath), p.GetValue(reviewAfter), p.GetValue(status), p.GetRequiredValue(reviewHash), ct)));
        root.Subcommands.Add(review);

        var list = new Command("list", "List the memory tree by canonical path, excluding archived nodes by default.");
        var scope = new Option<string?>("--scope"); var archived = new Option<bool>("--include-archived");
        list.Options.Add(scope); list.Options.Add(archived);
        list.SetAction((p, ct) => Run(p, json, formatter, () => service.ListAsync(Environment.CurrentDirectory, p.GetValue(scope), p.GetValue(archived), ct)));
        root.Subcommands.Add(list);

        var attention = new Command("attention", "Find overdue reviews, stale memories and missing working context.");
        var attentionScope = new Option<string?>("--scope"); var days = new Option<int>("--stale-days") { DefaultValueFactory = _ => 30 };
        attention.Options.Add(attentionScope); attention.Options.Add(days);
        attention.SetAction((p, ct) => Run(p, json, formatter, () => service.AttentionAsync(Environment.CurrentDirectory, p.GetValue(attentionScope), p.GetValue(days), ct)));
        root.Subcommands.Add(attention);

        var context = new Command("context", "Build a source-linked context pack within a character budget.");
        var contextPath = new Argument<string>("path"); var budget = new Option<int>("--max-characters") { DefaultValueFactory = _ => 6000 };
        var artifacts = new Option<string[]>("--artifact") { AllowMultipleArgumentsPerToken = true };
        context.Arguments.Add(contextPath); context.Options.Add(budget); context.Options.Add(artifacts);
        context.SetAction((p, ct) => Run(p, json, formatter, () => service.ContextAsync(Environment.CurrentDirectory, p.GetRequiredValue(contextPath), p.GetValue(budget), p.GetValue(artifacts), ct)));
        root.Subcommands.Add(context);

        var resume = new Command("resume", "Resume a node with bounded context, attention items and changes since its latest handoff.");
        var resumePath = new Argument<string>("path"); var resumeBudget = new Option<int>("--max-characters") { DefaultValueFactory = _ => 6000 };
        resume.Arguments.Add(resumePath); resume.Options.Add(resumeBudget);
        resume.SetAction((p, ct) => Run(p, json, formatter, () => service.ResumeAsync(Environment.CurrentDirectory, p.GetRequiredValue(resumePath), p.GetValue(resumeBudget), ct)));
        root.Subcommands.Add(resume);

        var handoffList = new Command("list", "List handoffs for one exact node, latest first.");
        var handoffPath = new Argument<string>("path"); handoffList.Arguments.Add(handoffPath);
        handoffList.SetAction((p, ct) => Run(p, json, formatter, () => service.HandoffsAsync(Environment.CurrentDirectory, p.GetRequiredValue(handoffPath), ct)));
        handoff.Subcommands.Add(handoffList);
        var show = new Command("show", "Show a node's latest handoff or select one by filename.");
        var showPath = new Argument<string>("path"); var handoffFile = new Option<string?>("--file");
        show.Arguments.Add(showPath); show.Options.Add(handoffFile);
        show.SetAction((p, ct) => Run(p, json, formatter, () => service.ReadHandoffAsync(Environment.CurrentDirectory, p.GetRequiredValue(showPath), p.GetValue(handoffFile), ct)));
        handoff.Subcommands.Add(show);

        var doctor = new Command("doctor", "Diagnose repository problems; --repair rebuilds derived indexes when sources are valid.");
        var repair = new Option<bool>("--repair"); doctor.Options.Add(repair);
        doctor.SetAction((p, ct) => Run(p, json, formatter, () => service.DoctorAsync(Environment.CurrentDirectory, p.GetValue(repair), ct)));
        root.Subcommands.Add(doctor);

        var import = new Command("import", "Preview importing one note at an explicit node path. Use --apply to save it after resolving conflicts.");
        var source = new Argument<string>("source"); var destination = new Argument<string>("path"); var apply = new Option<bool>("--apply");
        import.Arguments.Add(source); import.Arguments.Add(destination); import.Options.Add(apply);
        import.SetAction((p, ct) => Run(p, json, formatter, async () =>
        {
            var sourcePath = Path.GetFullPath(p.GetRequiredValue(source));
            var text = await File.ReadAllTextAsync(sourcePath, ct);
            return await service.ImportAsync(Environment.CurrentDirectory, p.GetRequiredValue(destination), sourcePath, text, File.GetLastWriteTimeUtc(sourcePath), p.GetValue(apply), ct);
        }));
        root.Subcommands.Add(import);
    }

    private static async Task<int> Run<T>(System.CommandLine.ParseResult parse, Option<bool> json, IHumanFormatter formatter, Func<Task<T>> action)
    {
        try
        {
            var result = await action();
            Console.WriteLine(parse.GetValue(json) ? JsonSerializer.Serialize(result, JsonOptions) : formatter.FormatWorkflow(result!));
            return result is DoctorReport { Validation.HasErrors: true } or ImportResult { CanApply: false } ? CliExitCodes.ValidationFailed : CliExitCodes.Success;
        }
        catch (Exception exception) when (CliOutput.IsUserError(exception))
        {
            CliOutput.WriteError(parse.GetValue(json), exception.Message);
            return CliExitCodes.UserError;
        }
    }

}
