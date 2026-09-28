# RAVEN Status

## Integrated

- Native company research: identity resolution, source discovery and review, acquisition, evidence persistence, profile generation, and explicit profile confirmation.
- Company Workspace: overview, sources, investigations, Briefings, profile changes, monitoring, lifecycle safety, and workspace review.
- Evidence-backed immutable Company Profile versions, targeted improvement, and deterministic change tracking.
- Managed AI Research through Exa Agent, bounded Gemini/MAF Deep Research, and provider-neutral External Research Assist. All produce reviewable investigation material and never accept profile facts automatically.
- Explicit Investigation origin, purpose, topics, and server-owned Done/Reopen and applied-profile state. Profile Improvement is readiness-gated; General Research is available without a usable accepted Profile.
- Research Briefings from hand-picked Investigations with controlled templates, immutable versions, source snapshots, newer-research suggestions, version comparison, and explicit research handoff.
- Company-scoped Ask RAVEN conversations with persisted messages, citations, research attachments, Web Search state, and web-evidence snapshots. Recent Chats and browser-local active pointers restore the last viewed conversation without copying messages into local storage.
- Browser dictation with an editable transcript, explicit accept/cancel, and a compact microphone test. Speech support and microphone access depend on the browser and device.
- Provider-routed Brave/Exa Search, Crawl4AI Local/Exa Contents crawling, Gemini inference, durable user activity, and best-effort developer execution telemetry.

## Current limitations

- Source chunks, embeddings, RAG, MCP, Crawl4AI Cloud, notifications, and advanced analytics are not implemented.
- Monitoring and in-process research workers run only while the API is running; queued background work is not durable across restart.
- Ask RAVEN can attach completed managed Investigations and Briefings as research context; other saved or external Investigation origins are not attachable through the current Chat contract. Attachments are persisted but do not create accepted profile truth.
- Briefing generation is an explicit, synchronous AI synthesis of persisted material; it does not search or verify sources. Research latest/gaps uses existing Deep Research or External Assist entry points.
- Gemini live transcription and read-aloud are not included. Browser dictation is the only speech provider in this release.

## Validation baseline

Release validation (28 September 2026):

- Backend Release build: passed; 336/336 tests passed.
- Frontend production build: passed; 95/95 tests passed. Existing React test fixtures emit non-failing `act(...)` warnings, and the production bundle reports a size advisory.
- EF migrations: applied to in-memory scratch SQLite; no pending model changes.
- Edge smoke: Company A/B chat restoration, navigation, reload, Recent Chats, Research Context attachment, Web state restoration, and API restart recovery passed. Investigations and Briefings tabs opened without resetting Chat. The test environment denied microphone access, so live dictation was not verified.
- GitHub Actions: the release branch CI result is pending push and remote execution.
- No live AI-provider calls were required or performed for this validation.

## Compatibility

Historical migrations remain unchanged. Legacy persisted provider identifiers are normalized to supported provider priorities when settings are read. Historical research and profile records remain readable; profile confirmation remains the sole accepted-profile mutation boundary.
