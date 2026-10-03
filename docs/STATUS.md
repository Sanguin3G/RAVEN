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
- Admin-only provider credential management for Brave, Exa, Gemini, Google Maps, and Crawl4AI. Workspace overrides are AES-256-GCM encrypted with a deployment-provided 32-byte key, loaded before environment/configuration fallback, and take effect without restart. Provider status responses never echo secrets; authenticated browser runtime config returns only the intentionally browser-visible Maps key and safe demo-mode boolean.

## Current limitations

- Release scope changed on 3 Oct 2026: prepare portable deployment and merge after validation; no actual cloud deployment is required. Live provider/deployed smoke remains unperformed and becomes an operator acceptance step when hosting is chosen.

- Normal Ask RAVEN responses remain request-bound; they can be stopped but do not continue as background Chat jobs.
- Briefing generation reuses saved material and does not search or independently verify sources. Background jobs use the API's in-process worker and provide no distributed-worker guarantee.
- Browser recognition and speech synthesis depend on browser/device support. Browser recognition may use the browser's speech service; Gemini speech requires server configuration, quota, and connectivity.
- Gemini Live/TTS paths were not live-provider tested in this validation. Gemini TTS preview was rate-limited during this pass; its Settings playback lifecycle is covered with deterministic tests.
- Persistent vector retrieval, embeddings, MCP, and advanced analytics are not implemented. Cloud Run crawler invocation is implemented as a deployment seam but has not yet been validated against the private deployed service.
- Portable Docker packaging, private Cloud Run crawler identity propagation, same-origin `/api` proxy, and curated seed tooling/data are implemented. Cloud Run remains an optional crawler/hosting adapter; remote Crawl4AI endpoint/token configuration also works without that adapter.
- Google Maps connection checks must be verified from a browser origin because the key is referrer-restricted; the Admin server test reports this limitation rather than claiming a server-side probe proves browser authorization.

## Security follow-up

- `frontend/.env.local` is no longer tracked and local env files are ignored. A Google Maps client key existed in repository history; rotate it and restrict it externally to the Maps Embed API and intended RAVEN origins/referrers. Production now reads the browser key through authenticated runtime config rather than `VITE_GOOGLE_MAPS_EMBED_API_KEY`.
- Set `RAVEN_CREDENTIAL_MASTER_KEY` as a deployment secret before enabling workspace credential overrides. Without a valid Base64 32-byte key, secret persistence is disabled and provider environment fallbacks still work.

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
- No deployed or live-provider smoke was performed. Cloud Run packaging and the same-origin production proxy are being ported after the structural checkpoint.

Provider credential checkpoint in progress — 29 Sep 2026, branch `refactor/deployment-readiness`:

- Added the `AddProviderCredentials` migration; EF Core reports no pending model changes.
- Release test build succeeded. Focused credential API/security tests passed (3/3): encrypted persistence and live precedence/removal, Admin-only authorization and Crawl4AI URL validation, Maps-only runtime config, and missing-master-key storage disablement.
- Focused Maps runtime-config frontend tests passed (3/3); frontend production build passed. Full backend/frontend suites are deferred to the planned final release-readiness validation.
- No live-provider or deployed Cloud Run smoke has been performed. Google Maps browser-origin restrictions and Cloud Run packaging/seed work remain to be validated.

## Cloud Run/demo tooling validation — 29 Sep 2026

- Selectively ported Cloud Run API/frontend packaging from the stale demo branch. The frontend Nginx container proxies same-origin `/api` requests to the runtime-configured API origin with buffering disabled for Chat streaming. Private Crawl4AI invocation uses a Google identity token in `X-Serverless-Authorization` and retains Crawl4AI's separate Bearer token.
- Added read-only SQLite inventory and explicit-allowlist demo export tooling. Export operates on a copy, removes non-allowlisted Companies through the existing lifecycle service, strips Identity users/provider credentials/transient work, and validates migrations, model state, integrity, foreign keys, and obvious sensitive storage. The curated artifacts were generated in the subsequent checkpoint below.
- Backend Release suite passed (356/356); focused Cloud Run/demo-seed tests passed (3/3); EF reports no pending model changes. A temporary 70mai-only export from the real workspace succeeded and the source database SHA-256 remained unchanged; the temporary artifact was removed. Frontend production build passed. The full frontend Vitest run was attempted twice (default and single-worker), but neither produced a result after several minutes, so both were stopped; frontend tests are not green for this checkpoint.
- GitHub PR #1 CI passed backend and frontend jobs, including Release tests/builds and both Docker image builds (run 36539587633). Docker and `gcloud` CLIs were unavailable on this machine at that checkpoint; fresh seeded API startup, private crawler invocation, proxy/SSE smoke, and deployed authentication remained unverified. The original requirement for live deployed smoke was superseded by the 3 Oct deployment-preparation scope below.
- A seven-Company local presentation artifact and matching hosted `raven.seed.db` have since been generated from the real local workspace using `DemoSeed/demo-selection.json`. Both passed `integrity_check` and `foreign_key_check`, contain no Identity users or provider credential records at export, and include no non-allowlisted Company. The public seed was additionally scanned across text columns for obvious API-key formats, private Windows paths, and excluded adult-company references; none were found. The local demo database is ignored by Git, has since received a local Admin through normal bootstrap, and has an integrity-checked local backup `backend/raven.demo.db.backup-20260929`. No suitable Briefing exists in the approved source records. Deployed smoke remains outstanding.
- The live local presentation will use the existing `backend/src/Raven.Api/raven.db`, not the curated copy; the curated `raven.demo.db` remains an optional fallback. At the last read-only check, the current local database held 33 Companies and zero Identity users, whereas the curated copy held seven Companies and one local Admin. The current local list includes records explicitly excluded from the hosted seed, so it is not presentation-sanitized. Bootstrap the Admin separately against whichever database the API actually uses.

## Portable preparation checkpoint — 3 Oct 2026

- Platform-independent API/frontend images and `backend/deploy/compose.yaml` document the same runtime contract for a Docker host, Railway, AWS, or Cloud Run: HTTPS ingress, private API proxy, one API replica, writable persistent SQLite, platform-managed secrets, and CPU for background workers. The existing private Cloud Run crawler adapter remains optional.
- Seed loading is explicit through `RAVEN_SEED_DATABASE=true`; ordinary production startup creates an empty workspace. Container Data Protection keys persist alongside SQLite so auth sessions survive API restarts. The frontend forwards the HTTPS scheme for secure cookie/antiforgery handling.
- Export finalization now runs SQLite `VACUUM` so removed data is not retained in free pages. The existing public seed was compacted from 13,348,864 to 4,845,568 bytes and passed integrity/FK checks with zero users and credentials. This operation did not touch the developer or local presentation databases.
- CI now checks container startup for empty and seeded workspaces, HTTPS-proxied Admin login, anonymous API rejection, SPA deep links, and session/data preservation across restart using disposable resources and no live providers. Validation results are recorded once the current checks complete. Actual deployment is deferred by user instruction.

## Compatibility

Historical migrations remain unchanged. Legacy persisted provider identifiers are normalized to supported provider priorities when settings are read. Historical research and profile records remain readable; explicit profile confirmation remains the accepted-profile mutation boundary.
