# RAVEN development guide

## Working agreement

Read `AGENTS.md`, `docs/STATUS.md`, and `docs/ROADMAP.md` before starting implementation. Inspect the worktree and reconcile documentation with reality before changing code.

The integration owner owns shared contracts, `Program.cs`, `docker-compose.yml`, the frontend router, EF migration ordering, and the status/control-plane documents. Feature work should expose narrow registration helpers instead of directly editing those hotspots.

## Architecture rules

- Keep search, crawling, AI models, embeddings, and MCP as independent capabilities.
- Fast Research is deterministic application orchestration. Deep Research is agentic and RAG-first.
- Preserve source evidence and create immutable profile versions; never overwrite history.
- External calls must be mockable; normal CI must not require real provider credentials.
- Record requested and actual providers, including fallback decisions.

## Completion

A feature is complete only when integrated, configured, resilient to expected failure, tested where appropriate, documented, and reflected in `STATUS.md`.
