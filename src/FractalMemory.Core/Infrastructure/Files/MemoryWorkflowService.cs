using System.Globalization;
using System.Text;
using FractalMemory.Core.Application.Services;
using FractalMemory.Core.Domain.Enums;
using FractalMemory.Core.Domain.Models;
using FractalMemory.Core.Infrastructure.Parsing;

namespace FractalMemory.Core.Infrastructure.Files;

public sealed class MemoryWorkflowService(
    IRepositoryService repositories, INodeService nodes, IFileSystemService files,
    IIndexService indexes, IValidationService validation, IStructuredMemoryService structured, IClock clock) : IMemoryWorkflowService
{
    private (string Root, string Storage) Repository(string wd)
    {
        var root = repositories.FindRepositoryRoot(wd) ?? throw new InvalidOperationException("No FractalMemory repository found. Run 'fm init' first.");
        return (root, repositories.GetStorageRoot(root));
    }

    public async Task<MemoryDocument> ReadAsync(string wd, string path, string file, string? section, CancellationToken ct)
    {
        var (_, storage) = Repository(wd);
        var node = await nodes.GetNodeAsync(wd, path, ct);
        file = ResolveFile(node, file);
        var fullPath = RepositoryPathGuard.ResolveContainedPath(storage, $"{node.RelativePath}/{file}");
        if (!files.FileExists(fullPath)) throw new InvalidOperationException($"Memory document '{node.RelativePath}/{file}' was not found.");
        var raw = await files.ReadAllTextAsync(fullPath, ct);
        return Document(node.RelativePath, file, raw, section);
    }

    private static string ResolveFile(MemoryNode node, string file)
    {
        file = file switch { "index" => node.IndexFileName, "state" => node.StateFileName, "timeline" => node.TimelineFileName, "decisions" => node.DecisionsFileName, _ => file };
        if (file.Contains('\\') || file.Split('/').Any(p => p is "" or "." or "..") || Path.IsPathRooted(file))
            throw new ArgumentException("Use a contract filename or an artifacts/ relative path without traversal.");
        var contract = new[] { node.IndexFileName, node.StateFileName, node.TimelineFileName, node.DecisionsFileName };
        if (!contract.Contains(file, StringComparer.Ordinal) && !file.StartsWith("artifacts/", StringComparison.Ordinal))
            throw new ArgumentException("Only index, state, timeline, decisions and artifacts/ documents are accessible.");
        if (!NodeService.IsContentExtension(file) && !Path.GetExtension(file).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Supported source formats are Markdown, HTML and plain text.");
        return file;
    }

    private static MemoryDocument Document(string path, string file, string raw, string? section)
    {
        var html = Path.GetExtension(file).ToLowerInvariant() is ".html" or ".htm";
        var content = raw;
        int? start = html ? null : 1;
        int? end = html ? null : MemoryMarkdown.Normalize(raw).Split('\n').Length;
        if (section is not null)
        {
            var selected = MemoryMarkdown.FindSection(html ? HtmlTextExtractor.ToText(raw) : raw, section);
            content = selected.Content;
            start = html ? null : selected.StartLine;
            end = html ? null : selected.EndLine;
        }
        return new(path, file, $"{path}/{file}", $"memory://document/{Uri.EscapeDataString(path)}/{Uri.EscapeDataString(file)}",
            MemoryMarkdown.Hash(raw), content, section, start, end);
    }

    public Task<MemoryWriteResult> SetSectionAsync(string wd, string path, string file, string section, string content, string expectedHash, CancellationToken ct) =>
        MutateAsync(wd, path, file, expectedHash, raw => MemoryMarkdown.ReplaceSection(raw, section, content), null, ct);

    public Task<MemoryWriteResult> AppendAsync(string wd, string path, string file, string content, string expectedHash, string? supersedes, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (file is not ("timeline" or "decisions")) throw new ArgumentException("Append supports timeline or decisions.");
        if (file == "timeline" && supersedes is not null) throw new ArgumentException("Only decisions can supersede a decision.");
        var id = file == "decisions" ? Guid.NewGuid().ToString("N") : null;
        return MutateAsync(wd, path, file, expectedHash, raw => file == "decisions"
            ? DecisionLog.Append(raw, content, id!, clock.UtcNow, supersedes)
            : raw.TrimEnd() + $"\n\n## {clock.UtcNow:O}\n\n{content.Trim()}\n", id, ct);
    }

    public async Task<IReadOnlyList<DecisionEntry>> DecisionsAsync(string wd, string path, bool includeSuperseded, CancellationToken ct)
    {
        var document = await ReadAsync(wd, path, "decisions", null, ct);
        return DecisionLog.Parse(document.Content).Where(d => includeSuperseded || d.Status == "active").ToArray();
    }

    public Task<MemoryWriteResult> ReviewAsync(string wd, string path, DateTimeOffset reviewAfter, NodeStatus? status, string expectedHash, CancellationToken ct)
    {
        if (status is not null && !Enum.IsDefined(status.Value)) throw new ArgumentException("Unknown node status.");
        if (reviewAfter <= clock.UtcNow) throw new ArgumentException("Review-after must be a future timestamp.");
        return MutateAsync(wd, path, "index", expectedHash, raw =>
        {
            var changes = new Dictionary<string, object?> { ["review_after"] = reviewAfter.ToString("O"), ["last_reviewed"] = clock.UtcNow.ToString("O") };
            if (status is not null) changes["status"] = status.Value.ToString().ToLowerInvariant();
            return MemoryMarkdown.SetMetadata(raw, changes);
        }, null, ct);
    }

    private async Task<MemoryWriteResult> MutateAsync(string wd, string path, string file, string expectedHash, Func<string, string> update, string? decisionId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedHash);
        var (root, storage) = Repository(wd);
        var config = await repositories.LoadConfigAsync(root, ct);
        var node = await nodes.GetNodeAsync(wd, path, ct);
        file = ResolveFile(node, file);
        if (!file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || file.StartsWith("artifacts/", StringComparison.Ordinal))
            throw new InvalidOperationException("Structured updates require a Markdown contract document. Source artifacts are preserved as imported.");
        var target = RepositoryPathGuard.ResolveContainedPath(storage, $"{node.RelativePath}/{file}");
        var lockPath = RepositoryPathGuard.ResolveContainedPath(storage, $"indexes/locks/{MemoryMarkdown.Hash(node.RelativePath + "/" + file)}.lock");
        files.CreateDirectory(Path.GetDirectoryName(lockPath)!);
        await using var lease = await AcquireLockAsync(storage, lockPath, ct);
        RepositoryPathGuard.EnsureContainedPath(storage, target);
        var raw = await files.ReadAllTextAsync(target, ct);
        if (!MemoryMarkdown.Hash(raw).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Document changed since it was read. Read it again and retry with its current hash.");
        var updated = update(raw);
        if (file == node.DecisionsFileName)
        {
            var priorIds = DecisionLog.Parse(raw).Select(d => d.Id);
            var updatedIds = DecisionLog.Parse(updated).Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
            if (priorIds.Any(id => !updatedIds.Contains(id)))
                throw new InvalidOperationException("Updates must preserve managed decision IDs and metadata. Use append with supersedes to replace a decision.");
        }
        if (config.Metadata.FrontMatter || MemoryMarkdown.Metadata(raw).Count > 0)
            updated = MemoryMarkdown.SetMetadata(updated, new Dictionary<string, object?> { ["last_updated"] = clock.UtcNow.ToString("O") });
        await files.WriteAllTextAsync(target, updated, ct);
        var warnings = await RefreshAfterWriteAsync(root, config, ct);
        return new(Document(node.RelativePath, file, updated, null), warnings, decisionId);
    }

    private static async Task<FileStream> AcquireLockAsync(string storage, string path, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            RepositoryPathGuard.EnsureContainedPath(storage, path);
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 100) { await Task.Delay(50, ct); }
        }
    }

    private async Task<IReadOnlyList<string>> RefreshAfterWriteAsync(string root, RepositoryConfig config, CancellationToken ct)
    {
        if (!config.Indexing.Enabled || !config.Indexing.RefreshOnWrite) return [];
        try { await indexes.RefreshAsync(root, ct); return []; }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or OperationCanceledException)
        { return [$"Content was saved, but index refresh did not complete: {exception.Message} Run 'fm doctor --repair'."]; }
    }

    public async Task<IReadOnlyList<NodeOverview>> ListAsync(string wd, string? scope, bool includeArchived, CancellationToken ct)
    {
        var (root, _) = Repository(wd);
        var all = await nodes.GetAllNodesAsync(root, ct, scope: scope is null ? null : nodes.NormalizeNodePath(scope));
        return all.Where(n => includeArchived || n.Metadata.Status != NodeStatus.Archived)
            .Select(n => new NodeOverview(n.RelativePath, n.Metadata.Title ?? n.RelativePath, n.Metadata.Status, n.Metadata.LastUpdated, n.Metadata.ReviewAfter)).ToArray();
    }

    public async Task<IReadOnlyList<AttentionItem>> AttentionAsync(string wd, string? scope, int staleDays, CancellationToken ct)
    {
        if (staleDays < 1 || staleDays > 36500) throw new ArgumentOutOfRangeException(nameof(staleDays), "Stale days must be between 1 and 36500.");
        var list = await ListAsync(wd, scope, false, ct);
        var result = new List<AttentionItem>();
        foreach (var item in list)
        {
            var reasons = new List<string>();
            var node = await nodes.GetNodeAsync(wd, item.Path, ct);
            if (item.ReviewAfter is { } date && date <= clock.UtcNow) reasons.Add($"Review overdue since {date:O}.");
            if (item.LastUpdated is null || item.LastUpdated < clock.UtcNow.AddDays(-staleDays)) reasons.Add("Memory has no recent update timestamp.");
            var context = structured.BuildAnswerContext(node, [], 3, 10);
            reasons.AddRange(context.MissingInformation.Select(m => $"Missing {m}."));
            if (reasons.Count > 0) result.Add(new(item.Path, reasons));
        }
        return result;
    }

    public async Task<ContextPack> ContextAsync(string wd, string path, int maxCharacters, IReadOnlyList<string>? artifacts, CancellationToken ct)
    {
        if (maxCharacters < 256 || maxCharacters > 1000000) throw new ArgumentOutOfRangeException(nameof(maxCharacters), "Character budget must be between 256 and 1000000.");
        var node = await nodes.GetNodeAsync(wd, path, ct);
        var parts = new List<(string Label, MemoryDocument Document)>();
        var state = await ReadAsync(wd, path, "state", null, ct);
        var semanticState = state.File.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? state.Content : HtmlTextExtractor.ToText(state.Content);
        foreach (var section in MemoryMarkdown.Sections(semanticState)
            .Where(s => new[] { "Current Objective", "Current Goal", "Active Constraints", "Next Best Actions", "Open Questions" }.Contains(s.Title, StringComparer.OrdinalIgnoreCase))
            .OrderBy(s => s.Title.Contains("Objective", StringComparison.OrdinalIgnoreCase) || s.Title.Contains("Goal", StringComparison.OrdinalIgnoreCase) ? 0 : s.Title.Contains("Constraints", StringComparison.OrdinalIgnoreCase) ? 1 : 2))
        {
            var content = MemoryMarkdown.Knowledge(section.Content).Trim();
            if (content.Length > 0) parts.Add((section.Title, state with { Content = content, Section = section.Title, StartLine = state.StartLine is null ? null : section.StartLine, EndLine = state.EndLine is null ? null : section.EndLine }));
        }
        var decisionDocument = await ReadAsync(wd, path, "decisions", null, ct);
        var managed = DecisionLog.Parse(decisionDocument.Content);
        if (managed.Count > 0)
        {
            foreach (var decision in managed.Where(d => d.Status == "active"))
            {
                var section = MemoryMarkdown.FindSection(decisionDocument.Content, $"Decision {decision.Id}");
                parts.Add(($"Active decision {decision.Id}", decisionDocument with { Content = decision.Content, Section = section.Title, StartLine = section.StartLine, EndLine = section.EndLine }));
            }
        }
        else
        {
            var decisions = MemoryMarkdown.Knowledge(node.DecisionsContent).Trim();
            if (decisions.Split('\n').Any(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith('#')))
                parts.Add(("Recorded decisions", decisionDocument with { Content = decisions }));
        }
        if (parts.Count == 0)
        {
            var cleanState = MemoryMarkdown.Knowledge(node.StateContent).Trim();
            parts.Add(("State (review before relying on it)", state with { Content = cleanState }));
        }
        parts = parts.OrderBy(p => p.Label.Contains("Objective", StringComparison.OrdinalIgnoreCase) || p.Label.Contains("Goal", StringComparison.OrdinalIgnoreCase) ? 0
            : p.Label.Contains("Constraints", StringComparison.OrdinalIgnoreCase) ? 1
            : p.Label.Contains("decision", StringComparison.OrdinalIgnoreCase) ? 2 : 3).ToList();
        foreach (var artifact in artifacts ?? [])
        {
            if (!artifact.StartsWith("artifacts/", StringComparison.Ordinal)) throw new ArgumentException("Context attachments must be artifacts/ paths.");
            parts.Add((artifact, await ReadAsync(wd, path, artifact, null, ct)));
        }
        var warnings = structured.BuildAnswerContext(node, [], 3, 10).MissingInformation.Select(m => $"Missing {m}.").ToList();
        if (node.Metadata.ReviewAfter <= clock.UtcNow) warnings.Add("The scheduled review is overdue.");
        if (node.Metadata.LastUpdated is null || node.Metadata.LastUpdated < clock.UtcNow.AddDays(-30)) warnings.Add("This memory has no recent update timestamp.");
        if (node.Metadata.Status == NodeStatus.Archived) warnings.Add("This memory is archived; verify that it still applies.");
        var header = $"# {node.RelativePath}\nStatus: {node.Metadata.Status} | Updated: {node.Metadata.LastUpdated?.ToString("yyyy-MM-dd") ?? "unknown"}\n";
        var flags = new List<string>();
        if (warnings.Any(w => w.StartsWith("Missing ", StringComparison.Ordinal))) flags.Add("incomplete context");
        if (node.Metadata.ReviewAfter <= clock.UtcNow) flags.Add("review overdue");
        if (node.Metadata.LastUpdated is null || node.Metadata.LastUpdated < clock.UtcNow.AddDays(-30)) flags.Add("stale");
        if (flags.Count > 0) header += "Attention: " + string.Join(", ", flags) + "\n";
        const string marker = "\n[Context truncated; read linked sources.]";
        var output = new StringBuilder();
        var sources = new List<MemoryDocument>();
        var omitted = new List<string>();
        var budget = maxCharacters - marker.Length;
        output.Append(Clip(header, budget));
        var truncated = output.Length < header.Length;
        foreach (var part in parts)
        {
            var prefix = $"\n## {part.Label}\nSource: {part.Document.SourcePath}" +
                (part.Document.StartLine is { } start ? $"#L{start}-{part.Document.EndLine}" : "") + "\n";
            var available = budget - output.Length - prefix.Length;
            if (available <= 0) { omitted.Add(part.Label); truncated = true; continue; }
            var excerpt = Clip(part.Document.Content, available);
            output.Append(prefix).Append(excerpt);
            sources.Add(part.Document with { Content = excerpt });
            if (excerpt.Length < part.Document.Content.Length) { omitted.Add(part.Label + " (partial)"); truncated = true; }
        }
        if (truncated) output.Append(marker);
        var text = output.ToString();
        return new(node.RelativePath, text, maxCharacters, text.Length, truncated, omitted, sources, warnings);
    }

    private static string Clip(string value, int length)
    {
        var count = Math.Min(value.Length, Math.Max(0, length));
        if (count > 0 && count < value.Length && char.IsHighSurrogate(value[count - 1])) count--;
        return value[..count];
    }

    public async Task<IReadOnlyList<HandoffEntry>> HandoffsAsync(string wd, string path, CancellationToken ct)
    {
        var (root, storage) = Repository(wd);
        path = nodes.NormalizeNodePath(path);
        var config = await repositories.LoadConfigAsync(root, ct);
        var directory = RepositoryPathGuard.ResolveContainedPath(storage, config.Handoffs.Directory);
        var result = new List<(HandoffEntry Entry, DateTimeOffset Modified)>();
        foreach (var fullPath in files.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly))
        {
            RepositoryPathGuard.EnsureContainedPath(storage, fullPath);
            var raw = await files.ReadAllTextAsync(fullPath, ct);
            var metadata = MemoryMarkdown.Metadata(raw);
            var belongs = metadata.TryGetValue("node_path", out var value) ? value?.ToString() == path
                : new[] { "index.md", "index.html", "state.md", "state.html" }.Any(f => raw.Contains($"- `{path}/{f}`", StringComparison.Ordinal));
            if (!belongs) continue;
            var date = metadata.TryGetValue("created_at", out var created) && DateTimeOffset.TryParse(created?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed : files.GetLastWriteTimeUtc(fullPath);
            result.Add((new(Path.GetFileName(fullPath), date, metadata.ContainsKey("source_hashes")), files.GetLastWriteTimeUtc(fullPath)));
        }
        return result.OrderByDescending(h => h.Entry.CreatedAt).ThenByDescending(h => h.Modified).ThenByDescending(h => h.Entry.File, StringComparer.Ordinal).Select(h => h.Entry).ToArray();
    }

    public async Task<string> ReadHandoffAsync(string wd, string path, string? file, CancellationToken ct)
    {
        var entries = await HandoffsAsync(wd, path, ct);
        file ??= entries.FirstOrDefault()?.File;
        if (file is null || !entries.Any(h => h.File == file)) throw new InvalidOperationException("No matching handoff for this node. Create one with 'fm handoff create'.");
        var (root, storage) = Repository(wd);
        var config = await repositories.LoadConfigAsync(root, ct);
        return await files.ReadAllTextAsync(RepositoryPathGuard.ResolveContainedPath(storage, $"{config.Handoffs.Directory}/{file}"), ct);
    }

    public async Task<ResumePacket> ResumeAsync(string wd, string path, int maxCharacters, CancellationToken ct)
    {
        var node = await nodes.GetNodeAsync(wd, path, ct);
        var context = await ContextAsync(wd, path, maxCharacters, null, ct);
        var latest = (await HandoffsAsync(wd, path, ct)).FirstOrDefault();
        var changes = new List<string>();
        var available = false;
        if (latest is not null)
        {
            var metadata = MemoryMarkdown.Metadata(await ReadHandoffAsync(wd, path, latest.File, ct));
            if (metadata.TryGetValue("source_hashes", out var value) && value is Dictionary<object, object> hashes)
            {
                available = true;
                var previous = hashes.ToDictionary(p => p.Key.ToString()!, p => p.Value?.ToString(), StringComparer.Ordinal);
                changes.AddRange(previous.Keys.Union(node.SourceHashes.Keys).Where(key => previous.GetValueOrDefault(key) != node.SourceHashes.GetValueOrDefault(key)).Order(StringComparer.Ordinal));
            }
        }
        return new(context, latest, changes, available, (await AttentionAsync(wd, node.RelativePath, 30, ct)).Where(a => a.Path == node.RelativePath).ToArray());
    }

    public async Task<DoctorReport> DoctorAsync(string wd, bool repair, CancellationToken ct)
    {
        try
        {
            var report = await validation.ValidateAsync(wd, ct);
            var rebuilt = false;
            if (repair && !report.HasErrors) { await indexes.RefreshAsync(wd, ct); rebuilt = true; report = await validation.ValidateAsync(wd, ct); }
            return new(report, rebuilt, report.HasErrors
                ? ["Correct the reported source/configuration errors, then rerun 'fm doctor --repair'. Source files are never deleted by repair."]
                : repair ? ["Indexes rebuilt from source documents."] : ["Use 'fm doctor --repair' to rebuild derived indexes."]);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or ArgumentException)
        {
            return new(new ValidationReport { Issues = [new() { RelativePath = "config.yaml", Severity = ValidationSeverity.Error, Message = exception.Message }] }, false,
                ["Check .fractal-memory/config.yaml and repository storage paths, then rerun doctor."]);
        }
    }

    public async Task<ImportResult> ImportAsync(string wd, string path, string sourceName, string content, DateTimeOffset? sourceModified, bool apply, CancellationToken ct)
    {
        var (root, storage) = Repository(wd);
        path = nodes.NormalizeNodePath(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        var extension = Path.GetExtension(sourceName).ToLowerInvariant();
        if (extension is not (".md" or ".html" or ".htm" or ".txt")) throw new ArgumentException("Import supports .md, .html, .htm and .txt notes.");
        var config = await repositories.LoadConfigAsync(root, ct);
        var importLock = RepositoryPathGuard.ResolveContainedPath(storage, "indexes/locks/import.lock");
        if (apply) files.CreateDirectory(Path.GetDirectoryName(importLock)!);
        await using var lease = apply ? await AcquireLockAsync(storage, importLock, ct) : null;
        var hash = MemoryMarkdown.Hash(content);
        var conflicts = new List<string>();
        if (files.DirectoryExists(RepositoryPathGuard.ResolveContainedPath(storage, path))) conflicts.Add($"Node '{path}' already exists; choose a new node path.");
        var duplicates = new List<string>();
        foreach (var existing in await nodes.GetAllNodesAsync(root, ct))
        {
            var source = RepositoryPathGuard.ResolveContainedPath(storage, $"{existing.RelativePath}/{existing.IndexFileName}");
            var meta = MemoryMarkdown.Metadata(await files.ReadAllTextAsync(source, ct));
            if (meta.TryGetValue("import_source_hash", out var old) && old?.ToString() == hash) duplicates.Add(existing.RelativePath);
        }
        var canApply = conflicts.Count == 0 && duplicates.Count == 0;
        var warnings = new List<string>();
        if (apply && canApply)
        {
            var artifact = "artifacts/source" + extension;
            var metadata = new Dictionary<string, object?>
            {
                ["title"] = Path.GetFileNameWithoutExtension(sourceName),
                ["status"] = "draft",
                ["last_updated"] = clock.UtcNow.ToString("O"),
                ["import_source"] = sourceName,
                ["import_source_hash"] = hash,
                ["import_source_modified"] = sourceModified?.ToString("O"),
            };
            var index = MemoryMarkdown.SetMetadata($"# Imported note\n\nSource: [{artifact}]({artifact})\n\nReview the original source before recording the current objective and constraints.\n", metadata);
            await nodes.CreateNodeAsync(root, path, ct, documents: new Dictionary<string, string> { ["index.md"] = index, [artifact] = content });
            warnings.AddRange(await RefreshAfterWriteAsync(root, config, ct));
        }
        return new(path, sourceName, hash, canApply, apply && canApply, conflicts, duplicates, warnings);
    }
}
