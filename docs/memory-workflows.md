# Memory workflows

FractalMem can now capture, maintain and resume local memory through both the CLI and MCP. All commands accept `--json`; successful data goes to stdout, and command errors go to stderr. Exit codes are 0 for success, 1 for validation/import conflicts, and 2 for invalid input or stale writes.

## Capture and update

```bash
fm node create projects/example
fm read projects/example --file state --json
fm update projects/example "Current Objective" "Ship the local memory workflow." --expected-hash <hash-from-read>
fm read projects/example --file timeline --json
fm append projects/example timeline "Validated the first working build." --expected-hash <timeline-hash>
```

Read the document first and use its `hash` as `--expected-hash`. Section reads return the hash of the **whole source document**, so they are also valid inputs to updates. Updates reject stale hashes. CLI and MCP writers coordinate through file locks and replace files atomically. A manual editor does not participate in these locks; coordinate manual edits with automated writes.

`update` replaces an existing, uniquely named section. It preserves unrelated sections and unknown YAML metadata values; metadata formatting may be normalized. Headings inside fenced code and comments are not section boundaries. Content cannot introduce headings at or above the target section's level. Updates apply to Markdown contract files; HTML and imported artifacts remain readable. A successful write returns the new hash and any index-refresh warning, so a failed refresh never masquerades as an unsaved edit.

MCP equivalents: `memory_node_create`, `memory_read`, `memory_update`, `memory_append`.

## Decisions that can be superseded

```bash
fm read projects/example --file decisions --json
fm append projects/example decisions "Keep memory local to the repository." --expected-hash <hash>
fm decisions projects/example --json
fm append projects/example decisions "Use the revised storage policy." --supersedes <active-decision-id> --expected-hash <latest-hash>
fm decisions projects/example --all
```

Managed decisions have stable IDs, timestamps, active/superseded status and a link to the decision they replace. Superseding and appending occur in one atomic write. History remains in `decisions.md`. Once managed decisions exist, answer context and handoffs use their active entries as the decision authority. Older free-form notes are preserved and remain available with `read`; migrate the decisions still in force by appending them. `decisions` lists managed entries only.

MCP equivalents: `memory_append` and `memory_decisions`.

## Freshness and attention

```bash
fm list --scope projects
fm attention --scope projects --stale-days 30
fm read projects/example --file index --json
fm review projects/example --after 2027-01-01T00:00:00Z --expected-hash <index-hash>
```

Reviews record `last_reviewed` and `review_after` on the index. Optional `--status active|paused|archived|draft` changes the node status. `attention` reports overdue reviews, old or missing update timestamps, and missing objective/decisions/constraints/actions. The bundled template instructions are treated as missing knowledge rather than recorded facts. Updates retain the newest timestamp across the four contract documents.

`list` and `attention` exclude archived nodes by default; `list --include-archived` includes them. Explicit reads and context requests can still retrieve archived nodes, and context identifies their status. Existing search and recent-activity commands remain historical discovery surfaces and may return archived nodes.

MCP equivalents: `memory_list`, `memory_attention`, `memory_review`.

## Bounded context and full sources

```bash
fm context projects/example --max-characters 6000
fm context projects/example --max-characters 6000 --artifact artifacts/source.md --json
fm read projects/example --file state --section "Current Objective"
fm read projects/example --file artifacts/source.md
```

Context prioritizes the objective, constraints, active decisions and next actions, with optional source artifacts. It reports source hashes and paths, included excerpts, omitted sections, freshness/missing-context warnings and truncation. `usedCharacters` counts UTF-16 code units in `text`; it is **not a token count**. The budget applies to `text`, including source labels and truncation markers. JSON metadata, duplicate source excerpts and MCP resource links are outside that budget. Budget limits are 256–1,000,000 characters.

Markdown sections cite original source lines. HTML sections use semantic text and omit line coordinates. Supported artifact formats are Markdown, HTML and plain text. Sources are restricted to the selected node's contract documents and `artifacts/` subtree; traversal and symbolic links are rejected.

MCP `memory_read` and `memory_context` return structured data plus native resource links. Follow `memory://document/{encoded-node-path}/{encoded-file}` to retrieve the original source, URL-encoding the two components independently. Timeline, decisions and nested artifact sources use the same resource endpoint.

## Resume the correct branch

```bash
fm handoff create projects/example
fm handoff list projects/example
fm handoff show projects/example
fm resume projects/example --max-characters 6000 --json
```

New handoffs persist the exact node path, creation time and hashes of the four contract documents. `resume` combines current context, attention items, the latest handoff for that exact node, and the contract files changed since that snapshot. An unchanged list does not compare arbitrary artifacts or child nodes. Legacy handoffs can still be listed/read when their source references identify the node, but explicitly report that a hash comparison is unavailable.

MCP equivalents: `memory_handoff_create`, `memory_handoff_list`, `memory_handoff_read`, `memory_resume`.

## Diagnose and repair

```bash
fm doctor
fm doctor --repair --json
```

Doctor explains configuration and source validation errors. Repair rebuilds derived indexes after source validation passes; it does not rewrite or delete source documents. Malformed configuration, invalid integer/boolean values, and incorrectly shaped sections are reported instead of silently adopting defaults. Index cache format 3 rebuilds older caches to include review metadata and updated parsing.

MCP equivalent: `memory_doctor` (`repair` defaults to false).

## Import existing notes

```bash
fm import ./existing-notes.md research/existing-notes --json
fm import ./existing-notes.md research/existing-notes --apply --json
```

The initial importer accepts one Markdown, HTML or text note and an explicit destination node path. Preview is the default. It reports destination conflicts and duplicate source hashes; either blocks an apply. Apply stages a complete draft node, preserves the original text under `artifacts/source.*`, and records the source name, source timestamp and import hash. The source is not summarized into assertions automatically: read it, then record the current objective and constraints through the update workflow.

MCP `memory_import` takes supplied `content` and `sourceName`, with optional `sourceModified`; it never reads arbitrary host files. `apply` defaults to false. Directory/bulk import and automatic hierarchy suggestions are future extensions.

### Template guidance and manual edits

Wrap custom scaffold instructions in `<!-- fractalmem-placeholder: your guidance -->`. Replace the entire comment when recording a fact. Marked guidance is excluded from context and answer fields, regardless of its wording; older bundled placeholder sentences are still recognized.

Curated state decisions are combined with active managed decisions. User-written index summaries provide an objective when the state has none; generated summaries do not. Context accepts the same heading aliases as structured retrieval.

If managed decision metadata is damaged by a manual edit, retrieval remains available and `doctor` reports the source error. Repair that metadata before updating the decision log; index repair does not rewrite source documents.
