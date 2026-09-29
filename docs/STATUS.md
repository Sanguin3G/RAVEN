# RAVEN Status

## Integrated

- Company Workspace for identity, sources, Investigations, Briefings, profile history, changes, monitoring, lifecycle safety, and workspace review.
- Evidence-backed immutable Company Profile versions, targeted improvement, and deterministic change tracking. Only explicit profile confirmation changes accepted Company truth.
- Managed Deep Research and External AI Assist produce reviewable Investigation material; they do not accept Profile facts automatically.
- Briefings synthesize selected Investigations into immutable versions. Create and Update run as persisted background jobs and appear in global Research Activity; failed updates leave the prior version intact.
- Company-scoped Ask RAVEN conversations persist messages, citations, Web snapshots, and Research Context. Recent Chats are restorable and deletable; the browser stores only per-company active IDs and unsent drafts.
- Ask RAVEN restores by explicit URL ID, browser pointer, then recent server conversation. It preserves Profile-version pinning and does not create empty conversations on workspace open.
- Research Context can attach completed saved or managed Investigations and pinned Briefing versions. These remain unaccepted research material.
- Search latest and Research further open editable confirmation states. Web Search or Deep Research starts only after confirmation; citations appear in the Sources disclosure without a duplicate source action.
- Completed Ask RAVEN messages have quiet copy, read-aloud, retry, and timestamp controls. User messages can be copied, edited and sent as a new turn, or asked again; Stop cancels a normal request-bound Chat turn.
- Browser dictation, Gemini Transcribe Live, browser speech synthesis, and Gemini TTS are user-triggered options in Voice & speech. Dictation stays editable and is never auto-sent; Read aloud is never automatic.
- Companies always opens `/companies`. Leaving a company for Dashboard, Research Company, System Status, Settings, Help, or About preserves the full route for a nested return item beneath Companies.
- Settings includes Research providers and Voice & speech; Custom routing is disclosed only for the Custom preset. No standalone Advanced section remains.
- About RAVEN and Help provide concise product/trust overviews, workflow guides, sticky topic navigation, and FAQ.
- Provider-routed Brave/Exa Search, Crawl4AI Local/Exa Contents, Gemini inference, durable user activity, and best-effort developer execution telemetry.
- Private ASP.NET Core Identity workspace with Admin/Researcher roles, cookie login/logout, password change, bootstrap Admin, member enable/role operations, default authenticated API authorization, and Admin-only merge/permanent delete. Researchers retain normal workspace access; cancelling a just-created initial research row archives it instead of invoking permanent deletion.

## Current limitations

- Normal Ask RAVEN responses remain request-bound; they can be stopped but do not continue as background Chat jobs.
- Briefing generation reuses saved material and does not search or independently verify sources. Background jobs use the API's in-process worker and provide no distributed-worker guarantee.
- Browser recognition and speech synthesis depend on browser/device support. Browser recognition may use the browser's speech service; Gemini speech requires server configuration, quota, and connectivity.
- Gemini Live/TTS paths were not live-provider tested in this validation. Gemini TTS preview was rate-limited during this pass; its Settings playback lifecycle is covered with deterministic tests.
- Persistent vector retrieval, embeddings, MCP, Crawl4AI Cloud, and advanced analytics are not implemented.

## Security follow-up

- `frontend/.env.local` is no longer tracked and local env files are ignored. A Google Maps client key existed in repository history; rotate it and restrict it externally to the required Google API and intended origins/referrers.

## Validation baseline

Structural modularization checkpoint — 29 Sep 2026, branch `refactor/deployment-readiness`, implementation commit `8ee90b7`:

- Backend: Release build passed; 344/344 tests passed.
- EF Core `has-pending-model-changes`: no model changes.
- Frontend: 107/107 tests across 20 files passed; production build passed. Vite reports the main JavaScript chunk at 824.52 kB, above its 500 kB advisory threshold.
- Edge manual smoke used an isolated SQLite backup of the local database: Companies list, a company Overview with restored Ask RAVEN history, Settings load, and the contextual company return link rendered. Desktop and 390 px mobile shell layouts rendered after the stylesheet move. No research/provider operation was started.
- Live providers were not called. The local crawler/provider status was unavailable during the smoke.
- `git diff --check`: passed. Current branch has not yet had a GitHub Actions run.

Authentication checkpoint — 29 Sep 2026, branch `refactor/deployment-readiness`:

- Backend Release build passed with no warnings; full backend suite passed (350/350), including six focused Identity/authorization tests.
- EF Core `has-pending-model-changes`: no model changes after the Identity migration.
- Frontend production build passed. The full suite initially reported 108/109 because one new permission test used a singular query for two same-name companies; that query was corrected and its file passed (6/6). Initial-research workflow tests passed (11/11) after cancellation cleanup changed to archive. The full frontend suite was not rerun after those targeted corrections.
- No deployed or live-provider smoke was performed. Cloud Run packaging and the same-origin production proxy remain pending.

## Compatibility

Historical migrations remain unchanged. Legacy persisted provider identifiers are normalized to supported provider priorities when settings are read. Historical research and profile records remain readable; explicit profile confirmation remains the accepted-profile mutation boundary.
