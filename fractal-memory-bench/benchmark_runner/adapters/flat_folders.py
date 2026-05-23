from __future__ import annotations

from pathlib import Path

from benchmark_runner.adapters.base import (
    MemoryApproachAdapter,
    keyword_overlap_score,
    render_joined_items,
)


class FlatFoldersAdapter(MemoryApproachAdapter):
    approach_name = "flat_folders"

    def retrieve(self, task_prompt: str, metadata: dict):
        assert self.config is not None
        query = " ".join(
            [
                task_prompt,
                metadata.get("project", ""),
                " ".join(metadata.get("must_recover", [])),
                " ".join(metadata.get("useful_to_recover", [])),
            ]
        )
        ranked: list[tuple[float, Path, str]] = []
        for path in self.iter_markdown_files():
            content = self.read_text(path)
            score = keyword_overlap_score(query, f"{self.relative_path(path)} {content}")
            if score > 0:
                ranked.append((score, path, content))
        top = sorted(ranked, key=lambda item: (-item[0], self.relative_path(item[1])))[: self.config.max_files_per_retrieval]
        items = [self.excerpt_item(path, content[:1200].strip(), score) for score, path, content in top]
        return self.build_result(
            items,
            render_joined_items(items),
            retrieval_operations=max(1, len(ranked)),
            metadata={"depth_used": "flat-search"},
        )
