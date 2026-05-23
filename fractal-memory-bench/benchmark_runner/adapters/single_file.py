from __future__ import annotations

from pathlib import Path

from benchmark_runner.adapters.base import (
    MemoryApproachAdapter,
    keyword_overlap_score,
    render_joined_items,
)


class SingleFileAdapter(MemoryApproachAdapter):
    approach_name = "single_file"

    def setup(self, corpus_path: str | Path, run_dir: str | Path, config) -> None:
        super().setup(corpus_path, run_dir, config)
        assert self.run_dir is not None
        self.run_dir.mkdir(parents=True, exist_ok=True)
        memory_file = self.run_dir / "MEMORY.md"
        chunks: list[str] = []
        for path in self.iter_markdown_files():
            chunks.append(f"# {self.relative_path(path)}\n\n{self.read_text(path).strip()}")
        memory_file.write_text("\n\n".join(chunks), encoding="utf-8")
        self._volatile_cache["memory_file"] = memory_file

    def retrieve(self, task_prompt: str, metadata: dict):
        memory_file: Path = self._volatile_cache["memory_file"]
        full_text = self.read_text(memory_file)
        lines = [line for line in full_text.splitlines() if line.strip()]
        query = " ".join([task_prompt, metadata.get("project", "")])
        scored = [
            (keyword_overlap_score(query, line), index, line)
            for index, line in enumerate(lines)
        ]
        top_lines = [line for score, _, line in sorted(scored, key=lambda item: (-item[0], item[1]))[:12] if score > 0]
        excerpt = "\n".join(top_lines) if top_lines else "\n".join(lines[:12])
        item = self.excerpt_item(memory_file, excerpt, score=1.0, relevant=None)
        item.path = "MEMORY.md"
        return self.build_result([item], render_joined_items([item]), retrieval_operations=1, metadata={"depth_used": "global"})
