from __future__ import annotations

from benchmark_runner.adapters.base import (
    MemoryApproachAdapter,
    keyword_overlap_score,
    render_joined_items,
)


class AutoRetrievalStubAdapter(MemoryApproachAdapter):
    approach_name = "auto_retrieval_stub"

    def retrieve(self, task_prompt: str, metadata: dict):
        assert self.config is not None
        query = " ".join([task_prompt, metadata.get("project", "")])
        ranked = []
        for path in self.iter_markdown_files():
            content = self.read_text(path)
            score = keyword_overlap_score(query, f"{self.relative_path(path)} {content}")
            if score > 0:
                ranked.append((score, path, content))

        top_n = min(len(ranked), self.config.max_files_per_retrieval + 2)
        top = sorted(ranked, key=lambda item: (-item[0], self.relative_path(item[1])))[:top_n]
        items = [self.excerpt_item(path, content[:900].strip(), score, relevant=None) for score, path, content in top]
        return self.build_result(
            items,
            render_joined_items(items),
            retrieval_operations=max(1, len(ranked)),
            metadata={"depth_used": "search-and-inject"},
        )
