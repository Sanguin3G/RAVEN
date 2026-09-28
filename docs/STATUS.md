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
- Cloud Run demo images use a bundled SQLite seed and ephemeral runtime storage. Runtime changes disappear when the API instance is replaced; request-based CPU does not guarantee in-process background work after the response.

## Validation baseline

Cloud Run demo packaging (branch `codex/cloud-run-demo`, 29 September 2026): clean SQLite seed generated with 30 migrations, one default Research Settings row, zero Companies/Profile versions/Chat conversations, and `PRAGMA integrity_check=ok`; backend Release build and 343/343 tests passed. Frontend production build and 95/95 tests passed after making the Maps test independent of a local `.env.local` key. Both Docker image builds passed; the API container returned `/health` 200, frontend deep links returned 200, Chat streaming CORS preflight returned 204, and the API saw a separate Crawl4AI container as available. The pinned Crawl4AI v0.9.3 image returned `/health` 200 and crawled example.com locally with and without an enlarged shared-memory setting. Cloud Run IAM, deployment, live browser research, and live AI providers remain unverified; the project owner will operate Google Cloud Console.

Release validation (28 September 2026):

- Backend Release build: passed; 336/336 tests passed.
- Frontend production build: passed; 95/95 tests passed. Existing React test fixtures emit non-failing `act(...)` warnings, and the production bundle reports a size advisory.
- EF migrations: applied to in-memory scratch SQLite; no pending model changes.
- Edge smoke: Company A/B chat restoration, navigation, reload, Recent Chats, Research Context attachment, Web state restoration, and API restart recovery passed. Investigations and Briefings tabs opened without resetting Chat. The test environment denied microphone access, so live dictation was not verified.
- GitHub Actions: backend and frontend passed on code commit `d8674cf` ([run 36377798696](https://github.com/Sanguin3G/RAVEN/actions/runs/36377798696)).
- No live AI-provider calls were required or performed for this validation.

Current `feat/fix-web-search-chatbot` branch (not yet merged into `main`):

- Ask RAVEN now hydrates pinned Profile collections, retrieves relevant passages from long profile sources, and answers short source-backed leader questions without Web Search.
- Chat Web Search plans bounded facet queries and ranks merged candidates with a claim-aware source authority policy.
- Backend Release build passed with no warnings or errors; backend tests passed 341/341. The new tests use fake providers; live Search/Crawl quality has not been verified.

## Compatibility

Historical migrations remain unchanged. Legacy persisted provider identifiers are normalized to supported provider priorities when settings are read. Historical research and profile records remain readable; profile confirmation remains the sole accepted-profile mutation boundary.
