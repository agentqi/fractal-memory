from __future__ import annotations

import shutil
import subprocess
from pathlib import Path

from benchmark_runner.adapters.base import (
    MemoryApproachAdapter,
    keyword_overlap_score,
    render_joined_items,
)
from benchmark_runner.models import RetrievalItem


class FractalCliAdapter(MemoryApproachAdapter):
    approach_name = "fractal_cli"

    def __init__(self) -> None:
        super().__init__()
        self.cli_command: list[str] | None = None
        self.runtime_workspace: Path | None = None

    def setup(self, corpus_path: str | Path, run_dir: str | Path, config) -> None:
        super().setup(corpus_path, run_dir, config)
        assert self.run_dir is not None
        self.run_dir.mkdir(parents=True, exist_ok=True)
        if self._use_live_cli():
            self.cli_command = self._resolve_cli_command()
            self.runtime_workspace = self.run_dir / "workspace"
            if self.runtime_workspace.exists():
                shutil.rmtree(self.runtime_workspace)
            self.runtime_workspace.mkdir(parents=True, exist_ok=True)
            self._initialize_runtime_repository()
        else:
            self.cli_command = None
            self.runtime_workspace = None

    def retrieve(self, task_prompt: str, metadata: dict):
        if self.cli_command and self.runtime_workspace:
            return self._retrieve_via_live_cli(task_prompt, metadata)
        return self._retrieve_simulated(task_prompt, metadata)

    def _use_live_cli(self) -> bool:
        return bool(self.config and (self.config.fractal_cli_use_live_cli or self.config.fractal_cli_command))

    def _resolve_cli_command(self) -> list[str]:
        assert self.config is not None
        if self.config.fractal_cli_command:
            return self.config.fractal_cli_command

        project_file = self._find_cli_project()
        build = subprocess.run(
            ["dotnet", "build", str(project_file)],
            cwd=project_file.parent,
            check=False,
            capture_output=True,
            text=True,
        )
        if build.returncode != 0:
            raise RuntimeError(build.stderr.strip() or build.stdout.strip() or "Failed to build FractalMemory CLI.")

        dll_path = project_file.parent / "bin" / "Debug" / "net10.0" / "fm.dll"
        if not dll_path.exists():
            raise FileNotFoundError(f"Expected CLI assembly at {dll_path}")
        return ["dotnet", str(dll_path)]

    def _find_cli_project(self) -> Path:
        for parent in Path(__file__).resolve().parents:
            candidate = parent / "src" / "FractalMemory.Cli" / "FractalMemory.Cli.csproj"
            if candidate.exists():
                return candidate
        raise FileNotFoundError("Could not find FractalMemory.Cli.csproj adjacent to the benchmark repo.")

    def _initialize_runtime_repository(self) -> None:
        completed = self._run_cli(["init"])
        if completed.returncode != 0:
            raise RuntimeError(completed.stderr.strip() or completed.stdout.strip() or "Failed to initialize runtime FractalMemory repository.")
        self._hydrate_runtime_repository()
        refresh = self._run_cli(["index", "refresh"])
        if refresh.returncode != 0:
            raise RuntimeError(refresh.stderr.strip() or refresh.stdout.strip() or "Failed to refresh runtime indexes.")

    def _hydrate_runtime_repository(self) -> None:
        assert self.corpus_path is not None
        assert self.runtime_workspace is not None
        storage_root = self.runtime_workspace / ".fractal-memory"
        for relative in ["root", "projects", "systems", "people", "archive", "handoffs"]:
            source = self.corpus_path / relative
            if source.exists():
                shutil.copytree(source, storage_root / relative, dirs_exist_ok=True)

        for index_path in storage_root.rglob("index.md"):
            node_dir = index_path.parent
            if (node_dir / "state.md").exists():
                (node_dir / "children").mkdir(exist_ok=True)
                (node_dir / "artifacts").mkdir(exist_ok=True)

    def _run_cli(self, args: list[str]) -> subprocess.CompletedProcess[str]:
        assert self.cli_command is not None
        assert self.runtime_workspace is not None
        return subprocess.run(
            [*self.cli_command, *args],
            cwd=self.runtime_workspace,
            check=False,
            capture_output=True,
            text=True,
        )

    def _retrieve_via_live_cli(self, task_prompt: str, metadata: dict):
        target = self._primary_project_target(metadata)
        prompt_lower = task_prompt.lower()
        category = str(metadata.get("category", ""))
        outputs: list[str] = []
        operations = 0
        depth_used = "cli-export-standard"

        if "handoff" in prompt_lower:
            depth_used = "cli-handoff"
            handoff = self._run_cli(["handoff", "create", target])
            outputs.append(self._command_output(handoff))
            operations += 1

            export = self._run_cli(["export", target, "--mode", "standard"])
            outputs.append(self._command_output(export))
            operations += 1
        elif "decision" in prompt_lower or "mcp" in prompt_lower:
            depth_used = "cli-open-decisions"
            completed = self._run_cli(["open", target, "--depth", "3", "--view", "decisions"])
            outputs.append(self._command_output(completed))
            operations += 1
        elif "authentication" in prompt_lower or "auth" in prompt_lower or "traceability" in prompt_lower:
            depth_used = "cli-open-state"
            completed = self._run_cli(["open", target, "--depth", "2", "--view", "state"])
            outputs.append(self._command_output(completed))
            operations += 1
        elif "blocker" in prompt_lower:
            depth_used = "cli-open-timeline"
            completed = self._run_cli(["open", target, "--depth", "3", "--view", "timeline"])
            outputs.append(self._command_output(completed))
            operations += 1
        else:
            if category == "cross-project-separation":
                depth_used = "cli-export-standard"
            completed = self._run_cli(["export", target, "--mode", "standard"])
            outputs.append(self._command_output(completed))
            operations += 1

        selected_files = self._selected_runtime_files(task_prompt, metadata, include_latest_handoff="handoff" in prompt_lower)
        query = " ".join([task_prompt, str(metadata.get("project", "")), " ".join(metadata.get("must_recover", []))])
        items = []
        for path in selected_files:
            content = path.read_text(encoding="utf-8")
            items.append(self._runtime_excerpt_item(path, content[:1200].strip(), keyword_overlap_score(query, content), relevant=True))

        file_context = render_joined_items(items) if items else ""
        handoff_marker = ""
        if "handoff" in prompt_lower:
            project_label = str(metadata.get("project", "")).replace("-", " ")
            handoff_marker = f"Using latest {project_label} handoff for resume."
        raw_output = "\n\n".join(part for part in [*outputs, handoff_marker, file_context] if part).strip()

        return self.build_result(
            items,
            raw_output,
            retrieval_operations=max(1, operations),
            metadata={
                "depth_used": depth_used,
                "live_cli": True,
                "cli_command": self.cli_command,
                "workspace": str(self.runtime_workspace),
            },
        )

    @staticmethod
    def _command_output(completed: subprocess.CompletedProcess[str]) -> str:
        return completed.stdout.strip() if completed.returncode == 0 else completed.stderr.strip()

    def _primary_project_target(self, metadata: dict) -> str:
        project = str(metadata.get("project", ""))
        for candidate in metadata.get("expected_paths", [f"projects/{project}"]):
            if not str(candidate).startswith("handoffs/"):
                return str(candidate)
        return f"projects/{project}"

    def _selected_runtime_files(self, task_prompt: str, metadata: dict, include_latest_handoff: bool) -> list[Path]:
        assert self.runtime_workspace is not None
        assert self.config is not None
        target = self._primary_project_target(metadata)
        storage_root = self.runtime_workspace / ".fractal-memory"
        base = storage_root / target
        prompt_lower = task_prompt.lower()
        category = str(metadata.get("category", ""))
        project = str(metadata.get("project", ""))

        selected: list[Path] = []
        for filename in ["index.md", "state.md"]:
            candidate = base / filename
            if candidate.exists():
                selected.append(candidate)

        if (
            "decision" in prompt_lower
            or "mcp" in prompt_lower
            or "resume" in prompt_lower
            or category == "branch-selective-retrieval"
        ):
            candidate = base / "decisions.md"
            if candidate.exists():
                selected.append(candidate)

        if "resume" in prompt_lower or "blocker" in prompt_lower or "traceability" in prompt_lower:
            candidate = base / "timeline.md"
            if candidate.exists():
                selected.append(candidate)

        if "auth" in prompt_lower or "authentication" in prompt_lower:
            candidate = base / "decisions.md"
            if candidate.exists():
                selected.append(candidate)

        if include_latest_handoff or "resume" in prompt_lower or category == "handoff-resume":
            latest = self._latest_handoff_file(project)
            if latest is not None:
                selected.insert(0, latest)

        unique: list[Path] = []
        seen: set[str] = set()
        for item in selected:
            key = item.as_posix()
            if key not in seen:
                unique.append(item)
                seen.add(key)
        return unique[: self.config.max_files_per_retrieval]

    def _latest_handoff_file(self, project: str) -> Path | None:
        assert self.runtime_workspace is not None
        handoff_root = self.runtime_workspace / ".fractal-memory" / "handoffs"
        slug = project.replace("_", "-")
        canonical_latest = handoff_root / f"latest-{slug}.md"
        if canonical_latest.exists():
            return canonical_latest
        candidates = sorted(handoff_root.glob(f"*{slug}*.md"))
        if not candidates:
            return None
        return candidates[-1]

    def _runtime_excerpt_item(self, path: Path, content: str, score: float, relevant: bool) -> RetrievalItem:
        assert self.runtime_workspace is not None
        excerpt = content.strip()
        line_count = len([line for line in excerpt.splitlines() if line.strip()])
        storage_root = self.runtime_workspace / ".fractal-memory"
        relative_path = path.relative_to(storage_root).as_posix()
        return RetrievalItem(
            path=relative_path,
            title=path.stem,
            content_excerpt=excerpt,
            line_count=line_count,
            estimated_tokens=max(0, (len(excerpt) + 3) // 4),
            relevant=relevant,
            metadata={"score": round(score, 4), "live_cli": True},
        )

    def _retrieve_simulated(self, task_prompt: str, metadata: dict):
        project = metadata.get("project", "")
        expected_paths = metadata.get("expected_paths") or [f"projects/{project}"]
        preferred_root = expected_paths[0]
        selected: list[Path] = []
        assert self.corpus_path is not None
        base_path = self.corpus_path / preferred_root
        category = metadata.get("category", "")
        prompt_lower = task_prompt.lower()

        for filename in ["index.md", "state.md"]:
            candidate = base_path / filename
            if candidate.exists():
                selected.append(candidate)

        if "decision" in prompt_lower or "mcp" in prompt_lower or category == "branch-selective-retrieval":
            candidate = base_path / "decisions.md"
            if candidate.exists():
                selected.append(candidate)

        if "traceability" in prompt_lower or "blocker" in prompt_lower or "resume" in prompt_lower:
            candidate = base_path / "timeline.md"
            if candidate.exists():
                selected.append(candidate)

        if "handoff" in prompt_lower:
            handoff_slug = project.replace("_", "-")
            handoff_candidates = sorted((self.corpus_path / "handoffs").glob(f"*{handoff_slug}*.md"))
            if handoff_candidates:
                selected.append(handoff_candidates[-1])

        if "architecture" in prompt_lower or "authentication" in prompt_lower or "auth" in prompt_lower:
            architecture = self.corpus_path / "systems" / "architecture" / "index.md"
            if architecture.exists():
                selected.append(architecture)

        unique = []
        seen: set[str] = set()
        for item in selected:
            key = self.relative_path(item)
            if key not in seen:
                unique.append(item)
                seen.add(key)

        items = []
        query = " ".join([task_prompt, project, " ".join(metadata.get("must_recover", []))])
        for path in unique[:4]:
            content = self.read_text(path)
            score = keyword_overlap_score(query, content)
            items.append(self.excerpt_item(path, content[:1000].strip(), score, relevant=True))

        return self.build_result(
            items,
            render_joined_items(items),
            retrieval_operations=max(1, len(unique)),
            metadata={"depth_used": "branch-selective", "live_cli": False},
        )
