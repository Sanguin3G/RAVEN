# RAVEN Status

## Integrated

- Company Workspace for identity, sources, Investigations, Briefings, profile history, changes, monitoring, lifecycle safety, and workspace review.
- Evidence-backed immutable Company Profile versions, targeted improvement, and deterministic change tracking. Only explicit profile confirmation changes accepted Company truth.
- Managed Deep Research and External AI Assist produce reviewable Investigation material; they do not accept Profile facts automatically.
- Briefings synthesize selected Investigations into immutable versions. Create and Update run as persisted background jobs and appear in global Research Activity; failed updates leave the prior version intact.
- Company-scoped Ask RAVEN conversations persist messages, citations, Web snapshots, and Research Context. Recent Chats are restorable and deletable; the browser stores only per-company active IDs and unsent drafts.
- Ask RAVEN restores by explicit URL ID, browser pointer, then recent server conversation. It preserves Profile-version pinning and does not create empty conversations on workspace open.
- Research Context can attach completed saved or managed Investigations and pinned Briefing versions. These remain unaccepted research material.
- Search latest and Research further open editable confirmation states. Web Search or Deep Research starts only after confirmation; source citations are exposed in the Sources disclosure without a duplicate source action.
- Browser dictation, Gemini Transcribe Live, browser speech synthesis, and Gemini TTS are user-triggered options in Voice & speech. Dictation stays editable and is never auto-sent; Read aloud is never automatic.
- Companies always opens `/companies`. Leaving a company for Settings or System Status can expose an explicit Back to <company> link that restores the full route from tab-scoped browser state.
- Settings includes Research providers and Voice & speech; Custom routing is disclosed only for the Custom preset. No standalone Advanced section remains.
- Provider-routed Brave/Exa Search, Crawl4AI Local/Exa Contents, Gemini inference, durable user activity, and best-effort developer execution telemetry.

## Current limitations

- Normal Ask RAVEN streaming remains tied to its request; it is not a background Chat job.
- Briefing generation reuses saved material and does not search or independently verify sources. Background jobs use the API's in-process worker and provide no distributed-worker guarantee.
- Browser recognition and speech synthesis depend on browser/device support. Browser recognition may use the browser's speech service; Gemini speech requires server configuration, quota, and connectivity.
- Gemini Live/TTS paths were not live-provider tested in this validation. The Settings copy discloses that Gemini receives audio or text and that free-tier requests may be used to improve Google products.
- Persistent vector retrieval, embeddings, MCP, Crawl4AI Cloud, and advanced analytics are not implemented.

## Validation baseline

Release validation — 28 Sep 2026:

- Backend Release build: passed. Backend Release tests: 339/339 passed.
- Frontend production build: passed. Frontend tests: 100/100 passed across 19 files. Vite reports the existing advisory that the main JavaScript chunk is larger than 500 kB.
- EF Core: all migrations applied to an in-memory SQLite database; `has-pending-model-changes` reported no model changes. EF tooling 10.0.10 reported that runtime 10.0.11 is newer.
- Edge smoke (fixture-backed): Ask RAVEN Search latest and Research further confirmation/cancel flows passed; no duplicate Show web sources action appeared; Research Context and Web state survived reload; company workspace tab changes preserved Chat; Settings/System Status return restored the full company route; Companies returned to the list. Browser microphone recognition and live speech providers were not manually exercised.
- Live providers: no Gemini or external search provider calls were made.
- GitHub Actions: the existing workflow covers backend/frontend on push to this branch; final pushed-commit result is pending.
- `git diff --check`: passed.

## Compatibility

Historical migrations remain unchanged. Legacy persisted provider identifiers are normalized to supported provider priorities when settings are read. Historical research and profile records remain readable; explicit profile confirmation remains the accepted-profile mutation boundary.
