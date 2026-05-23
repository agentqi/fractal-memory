# Architecture Notes

Shared architecture guidance:

- benchmark harnesses should use adapters, not hard-coded approach logic
- MCP tools should be thin wrappers over core services
- avoid cross-project contamination in retrieval flows
