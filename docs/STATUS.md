# RAVEN Status

## Integrated

- Native company research: identity resolution, source discovery and review, acquisition, evidence persistence, profile generation, and explicit profile confirmation.
- Company Workspace: overview, sources, investigations, Briefings, profile changes, monitoring, lifecycle safety, and workspace review.
- Evidence-backed immutable Company Profile versions, targeted improvement, and deterministic change tracking.
- Managed AI Research through Exa Agent, bounded Gemini/MAF Deep Research, and provider-neutral External Research Assist. All produce reviewable investigation material and never accept profile facts automatically.
- Explicit Investigation origin, purpose, topics, and server-owned Done/Reopen and applied-profile state. Profile Improvement is readiness-gated; General Research is available without a usable accepted Profile.
- Research Briefings from hand-picked Investigations with controlled templates, immutable versions, source snapshots, newer-research suggestions, version comparison, and explicit research handoff.
- Ask RAVEN conversations with persisted profile citations, bounded excerpts, optional Web Search, and persisted web-evidence snapshots.
- Provider-routed Brave/Exa Search, Crawl4AI Local/Exa Contents crawling, Gemini inference, durable user activity, and best-effort developer execution telemetry.

## Current limitations

- Source chunks, embeddings, RAG, MCP, Crawl4AI Cloud, notifications, and advanced analytics are not implemented.
- Monitoring and in-process research workers run only while the API is running; queued background work is not durable across restart.
- Investigation attachment grounding remains an Ask RAVEN context-extension seam; attachments are persisted but do not create accepted profile truth.
- Briefing generation is an explicit, synchronous AI synthesis of persisted material; it does not search or verify sources. Research latest/gaps uses existing Deep Research or External Assist entry points.

## Validation baseline

The integrated baseline previously passed backend build/tests, frontend type checks and build, focused Vitest coverage, and Edge smoke flows. Provider tests are deterministic and do not require credentials or paid provider traffic.

## Compatibility

Historical migrations remain unchanged. Legacy persisted provider identifiers are normalized to supported provider priorities when settings are read. Historical research and profile records remain readable; profile confirmation remains the sole accepted-profile mutation boundary.
