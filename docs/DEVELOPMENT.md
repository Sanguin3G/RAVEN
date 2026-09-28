# RAVEN Development

## Requirements

Install the .NET 10 SDK, Node.js, Docker Engine, and Git. Live provider use also needs server-side credentials for Brave and Gemini plus a matching Crawl4AI Local token.

## Local startup

From the repository root, start Crawl4AI Local:

```powershell
docker compose up -d crawl4ai
```

Start the API from `backend`:

```powershell
dotnet restore Raven.sln
dotnet run --project src/Raven.Api
```

Start the frontend from `frontend`:

```powershell
npm install
npm run dev
```

The frontend uses port 5173. The API uses `http://localhost:5180`, serves OpenAPI at `/openapi/v1.json` in development, and exposes `/health` (plus `/api/health` for the frontend's proxied status check).

## API surface

Core Company endpoints:

```text
POST /api/companies
GET  /api/companies
GET  /api/companies/{id}
POST /api/companies/matches
```

Staged research endpoints:

```text
POST /api/companies/{id}/research/discover
POST /api/companies/{id}/research/start            (202; in-process background discovery)
POST /api/companies/{id}/research/targeted
POST /api/companies/{id}/archive
POST /api/companies/{id}/restore
DELETE /api/companies/{id}                 (requires `{ "confirm": true }`)
POST /api/companies/merge/preview
POST /api/companies/merge/confirm
POST /api/companies/workspace-review
POST /api/companies/workspace-review/acknowledge
POST /api/companies/workspace-review/cleanup
POST /api/companies/{id}/research                 (legacy discover + auto-acquire)
GET  /api/companies/{id}/research-runs
GET  /api/research-runs/{id}
POST /api/research-runs/{id}/cancel
GET  /api/research-runs/active
GET  /api/research-runs/{id}/candidates
POST /api/research-runs/{id}/acquire
GET  /api/research-runs/{id}/sources
GET  /api/companies/{id}/sources
GET  /api/research-runs/{id}/coverage
GET  /api/companies/{id}/coverage
GET  /api/sources/{id}
POST /api/companies/{id}/deep-research
GET  /api/deep-research-runs/{id}
GET  /api/companies/{id}/deep-research-runs
POST /api/companies/{id}/saved-research
GET  /api/companies/{id}/saved-research
```

External Research Assist endpoints:

```text
POST /api/companies/{companyId}/external-research/brief
POST /api/companies/{companyId}/external-research/import
POST /api/companies/{companyId}/external-research/import/preview
POST /api/companies/{companyId}/external-research/analyze
GET  /api/companies/{companyId}/external-research/analyze/{jobId}
```

Managed AI Research endpoints:

```text
POST /api/companies/{companyId}/managed-research
GET  /api/companies/{companyId}/managed-research/{jobId}
GET  /api/companies/{companyId}/managed-research
GET  /api/companies/{companyId}/research-context-attachments
POST /api/companies/{companyId}/managed-research/{investigationId}/context-attachments
DELETE /api/companies/{companyId}/managed-research/{investigationId}/context-attachments
POST /api/companies/{companyId}/managed-research/{jobId}/cancel
```

Profile endpoints:

```text
POST /api/research-runs/{id}/profile/generate
GET  /api/research-runs/{id}/profile/candidate
POST /api/research-runs/{id}/profile/confirm
POST /api/research-runs/{id}/profile-patch/generate
POST /api/research-runs/{id}/profile-patch/confirm
GET  /api/companies/{id}/profile
```

`ResearchMode.TargetedEnrichment` is used both for accepted-profile patches and for an optional first-profile strengthening pass. With no base profile, generation combines the original approved Company evidence with the newly acquired target evidence. With a base profile, only the server-owned patch confirmation endpoint may append a replacement version.

System endpoints:

```text
GET /api/system/crawler-status
GET /api/system/provider-status
PUT /api/system/model-preferences
```

The runtime model-preference endpoint is a local compatibility surface. Current persistent model roles are configured through `/api/settings/research` and selected from the compatibility-tested catalog returned by `/api/settings/ai-models`. Neither endpoint accepts provider credentials.

## Configuration and secrets

Ask RAVEN stores Chat rows in SQLite across API restarts and pins each conversation to its accepted Profile version. `GET /api/companies/{companyId}/chat/conversations` returns 20 lightweight Company-scoped summaries; `GET /api/companies/{companyId}/chat/conversations/{conversationId}` hydrates messages and citations. The browser uses `raven:active-chat-by-company` only to choose which persisted conversation to reopen and `raven:chat-draft:<companyId>:<conversationId-or-new>` for optional unsent text.

Never commit a populated `.env` file. RAVEN does not load `.env` automatically; export values into the API process or use user secrets.

| Capability | Environment variables |
| --- | --- |
| Brave | `BRAVE_SEARCH_API_KEY` |
| Exa Search and Contents | `EXA_API_KEY` |
| Exa Managed AI Research | `EXA_API_KEY` |
| Google Maps Embed (optional frontend) | `VITE_GOOGLE_MAPS_EMBED_API_KEY` |
| Crawl4AI Local | `CRAWL4AI_LOCAL_BASE_URL`, `CRAWL4AI_API_TOKEN` |
| Gemini | `GEMINI_API_KEY`, optional `GEMINI_FAST_MODEL`, `GEMINI_DEEP_MODEL` |
| SQLite | `ConnectionStrings__Raven` |

The equivalent nested configuration sections remain available for local configuration. Provider keys are server-only and must never be returned to React, written to ResearchEvents, or added to source control.

Research Settings persist safe model roles, identity-resolution/reranking preferences, provider priorities, and provider-neutral Managed AI Research depth in SQLite. They never persist provider keys. `RAVEN Local First` uses Brave plus Crawl4AI Local; Resilient and Cloud presets use Brave/Exa Search and Crawl4AI Local/Exa Contents. Legacy persisted provider identifiers are normalized safely on read. Managed Research depth maps internally to Exa Agent effort (`Adaptive=auto`, `Focused=low`, `Standard=medium`, `Thorough=high`, `Exhaustive=xhigh`). Authentication, configuration, and invalid-request errors never silently fall back.

## Identity preflight API

`POST /api/research/identity/resolve` accepts only company identity hints (`name`, optional legal name, website, country, registration number, headquarters, and research hint). It resolves a strong explicit identifier without AI or otherwise makes one model-assisted topology attempt. It never creates a Company or ResearchRun and never invokes Search, Crawl, or profile generation. The response is a safe workflow state plus bounded identity options; model-provided fields are navigation hints, not verified profile facts. Gemini returns topology (`SpecificEntity`, `CorporateFamilyShorthand`, `NameCollision`, or `Unknown`); deterministic RAVEN policy derives the workflow status. `guidedRefinement=true` is advice-only and always returns no selectable target, so the user can edit the original form and submit a new attempt. `confirmExactName=true` is the deliberate manual override for an obscure or unresolved name.

## Docker

`docker-compose.yml` runs `unclecode/crawl4ai:latest` and exposes its port locally. Current Crawl4AI images need `CRAWL4AI_API_TOKEN` for host traffic. Give the API the same token and keep production values distinct from development values.

```powershell
docker compose logs crawl4ai
```

Do not silently substitute a cloud crawler if the local crawler fails; surface the source-level failure and continue eligible work.

### Cloud Run demo: three services

This is a short-lived demo deployment. The API still uses EF Core SQLite. `src/Raven.Api/seed/raven.seed.db` is a clean, versioned starter database generated from the current migrations with one default Research Settings row and no Company, Profile, Chat, or provider key data. The API copies it to `/app/data/raven.db` only when that runtime file is absent, then applies pending migrations. Do **not** copy the ignored local `src/Raven.Api/raven.db` into an image. A Cloud Run instance's filesystem is disposable: a replacement instance starts again from the seed, so any research, profile, chat, or settings change made during the demo is lost. Maximum instances is one to avoid concurrent independent SQLite databases. A new revision can temporarily overlap the previous revision; route 100% of traffic to the new revision after deployment.

To regenerate the seed after new migrations, move the existing seed to a temporary backup outside the repository, then from `backend` run `dotnet run --project SeedDatabase -- src/Raven.Api/seed/raven.seed.db`. The generator refuses to overwrite an existing file. Before committing, verify SQLite integrity, migration count, the single settings row, and absence of business records and secret values. Restore the backup if validation fails.

The Docker build contexts are the `backend` and `frontend` directories. Locally, after starting Docker Engine, build and smoke-test:

```powershell
docker build -t raven-api:demo backend
docker run --rm -p 8080:8080 raven-api:demo
# In another terminal: curl.exe http://localhost:8080/health

docker build --build-arg VITE_API_BASE_URL=http://localhost:8080 -t raven-frontend:demo frontend
docker run --rm -p 8081:8080 raven-frontend:demo
# In another terminal: curl.exe http://localhost:8081/companies
```

For a local API run with persistent demo data between container restarts, bind-mount a dedicated host directory at `/app/data`; do not use that local option as a Cloud Run persistence claim. The frontend Docker build requires `VITE_API_BASE_URL` because the production Vite bundle does not use the local dev proxy. If Maps Embed is needed, supply a separately restricted browser key at build time; never pass server provider keys to Vite.

#### Google Cloud Console setup

In your Google Cloud project, enable billing and the Cloud Run, Cloud Build, Artifact Registry, and Secret Manager APIs. In **Artifact Registry**, create a Docker repository named `raven` in your chosen region. Create a service account named `raven-api` for the API. Add provider keys used by the demo to **Secret Manager** (`GEMINI_API_KEY`, `BRAVE_SEARCH_API_KEY`, optionally `EXA_API_KEY`) and create one strong `CRAWL4AI_API_TOKEN` plus a separate crawler `SECRET_KEY`; grant the API service account Secret Manager Secret Accessor on its required secrets. Grant that service account **Cloud Run Invoker** on the Crawl4AI service only. Use the same crawler token secret for API and Crawl4AI; keep all keys out of Git, Cloud Build substitutions, and frontend build args.

From Cloud Shell in the Console, check out a repository revision that contains these deployment files and the tracked SQLite seed (or upload the same source tree). Then build the API image, replacing the placeholders with your project, region, and a unique tag:

```bash
REGION=asia-southeast1
PROJECT_ID=your-project-id
TAG=demo-1
gcloud builds submit backend --tag "$REGION-docker.pkg.dev/$PROJECT_ID/raven/raven-api:$TAG"
```

Create the **Crawl4AI** Cloud Run service first, from the pinned Linux amd64 upstream image `docker.io/unclecode/crawl4ai@sha256:84751dab794259db05d5bd4e5c766a8041a65f0554326e4516e620abdf2fa18b` (v0.9.3, locally tested 29 September 2026). Set container port `11235`, memory initially `4 GiB`, CPU `2`, minimum instances `0`, maximum instances `1`, and **Require authentication**. Bind `CRAWL4AI_API_TOKEN` and `SECRET_KEY` from Secret Manager. Confirm its service URL in the Console. Its `/health` is private; a browser request without Google identity should be denied. The pinned image digest makes the deployed image reproducible, but browser crawling still needs a live smoke test on Cloud Run.

Create the **API** Cloud Run service from the Artifact Registry image, container port `8080`, memory initially `1 GiB`, CPU `1`, minimum instances `0`, maximum instances `1`, request-based billing, **Allow unauthenticated invocations**, and the `raven-api` service account. Set `CRAWL4AI_LOCAL_BASE_URL` and `CRAWL4AI_CLOUD_RUN_AUDIENCE` both to the crawler's root service URL (no `/health` suffix), and set `Crawl4AI__Local__TimeoutSeconds=180` to allow for crawler cold start. Bind `CRAWL4AI_API_TOKEN` and the required provider keys from Secret Manager. The image sets `ConnectionStrings__Raven=Data Source=/app/data/raven.db`. The API requests a Google ID token from its Cloud Run metadata server and sends it to the crawler in `X-Serverless-Authorization`, leaving `Authorization: Bearer <crawler token>` for Crawl4AI itself. Record the API URL.

Build the frontend image **after** the API URL is known. `frontend/cloudbuild.yaml` passes the API URL as a Vite build argument; it is a public URL, not a secret:

```bash
API_URL=https://your-api-service-url
gcloud builds submit frontend --config frontend/cloudbuild.yaml \
  --substitutions "_IMAGE=$REGION-docker.pkg.dev/$PROJECT_ID/raven/raven-frontend:$TAG,_API_BASE_URL=$API_URL"
```

Create the **frontend** Cloud Run service from that image, container port `8080`, minimum instances `0`, **Allow unauthenticated invocations**. Record its URL. Then edit the API service and set `Cors__AllowedOrigins__0` to that exact frontend origin (scheme and host, without a trailing slash); deploying this change creates a new API revision and resets its ephemeral SQLite state. The same CORS policy covers JSON API requests and the Chat streaming endpoint. A reload at `/companies` must return the SPA, not an Nginx 404.

#### Acceptance checks after deployment

Check API `/health` and `/api/health`, then the frontend's deep-link reload and provider status page. `GET /api/system/crawler-status` through the API must report the private crawler available; direct unauthenticated browser access to the crawler should return 401/403. Perform one authorized crawl/research request, verify the resulting source, and exercise a Chat stream with provider keys configured. Restart or replace the API instance to confirm that runtime data returns to the seed. Check Cloud Run logs for startup, migrations, token failures, crawler browser failures, and CORS errors without logging any token value.

This low-cost profile uses zero minimum instances and request-based CPU. It does not guarantee that Monitoring, Deep Research, or other in-process queue workers continue after the initiating request returns. Minimum instances greater than zero can incur idle charges; consult [Cloud Run pricing](https://cloud.google.com/run/pricing) before enabling them for a longer demonstration.

## Database

SQLite is the source of record. API startup applies EF Core migrations. With the documented backend startup command, the default file is `backend/src/Raven.Api/raven.db`.

```powershell
# from backend
dotnet ef migrations add <MigrationName> --project src/Raven.Api --startup-project src/Raven.Api
dotnet ef database update --project src/Raven.Api --startup-project src/Raven.Api
```

## Tests and checks

`.github/workflows/ci.yml` runs the backend Release restore/build/test and frontend `npm ci`/test/build on pull requests to `main`, pushes to `main` or the release branch, and manual dispatch. No provider keys are required.

Browser dictation uses feature-detected Web Speech recognition and a secure-context microphone permission. The composer keeps transcripts editable and requires manual Send. The optional microphone test uses `getUserMedia` and `AnalyserNode`; its selected test device and language are stored in `raven:speech-preferences`. Browser recognition uses the system default microphone. Gemini speech and read aloud are not configured.

```powershell
# from backend
dotnet build Raven.sln
dotnet test Raven.sln

# from frontend
npm test -- --run
npm run build
```

Provider tests must use fakes, mocks, or fixtures. Normal automated tests must not require live provider credentials, paid traffic, TopCV availability, LinkedIn access, or a running crawler.

Microsoft Edge completion checks use the explicit Playwright `msedge` project (`project.use.channel = "msedge"`):

```powershell
# from frontend
npx playwright test --project=msedge
```

Chromium is not a substitute. The repository does not currently ship live-provider fixtures, so external-provider checks must be separately marked as controlled live smoke tests.

## Execution telemetry

Execution telemetry is diagnostic rather than product state. Search, crawl, and AI instrumentation sanitizes and enqueues canonical operation rows to a bounded in-process buffer; a hosted worker creates a fresh EF scope and saves batches. Queue saturation drops diagnostics and logs a bounded counter, never research/chat work. Terminal research/profile boundaries request a best-effort flush; host shutdown completes and drains the telemetry channel where time permits. Do not capture a request-scoped `RavenDbContext` in telemetry background work.

## Controlled identity-model probe

`backend/tools/IdentityProbe` is a non-production identity-model diagnostic tool. It exercises the configured `IAiModelProvider`/Gemini path with identity hints only: it creates no Company or ResearchRun and makes no Search, Crawl, or evidence calls. It is a controlled live smoke, not an automated test.

```powershell
# from repository root; avoid rebuilding Raven.Api if a local API process holds its executable
dotnet build backend/tools/IdentityProbe/IdentityProbe.csproj --no-restore -p:BuildProjectReferences=false
dotnet run --project backend/tools/IdentityProbe/IdentityProbe.csproj --no-build
```

It uses the existing Raven API user secret or `GEMINI_API_KEY`, defaults to `gemini-3.5-flash-lite`, and emits only sanitized semantic summaries, timing, token usage, parse state, and safe failure codes. Never commit credentials or raw model responses.

## Monitoring and Deep Research

Monitoring and Deep Research use in-process `BackgroundService` workers. They execute only while the API process is running; this project deliberately does not add an external scheduler or job broker. Monitoring produces a review-ready profile candidate and never accepts a profile automatically.

The background research start endpoint also uses an in-process channel-backed worker. It returns `202 Accepted`, exposes active runs, and supports cancellation. It is not a durable job queue: a process restart drops queued work, by design for this MVP.

The frontend persists only an active, resumable research session for handoff across navigation/reload. Cancelling a run clears that session and resets the research screen to its blank default state. A missing initial run or deleted company is discarded locally as stale session state; if a known run exists but a related restore request fails, the run stays in session and the UI shows the failing restore step with retry instead of converting it into `RESEARCH FAILED` or the generic API-unavailable message.

## Workspace lifecycle behavior

`POST /api/companies/merge/confirm` is a user-confirmed, transactional merge. It preserves compatible dependent records and profile history; moved serialized profile and candidate payloads are rewritten to the canonical company and combined profile versions are re-numbered in confirmation-time order. The frontend routes a completed merge to the canonical dossier.

Company List actions are addressable: **Monitor company** opens the Monitoring tab and **Improve profile** opens the targeted enrichment dialog. Profile Improvement requires a supported model-created baseline with at least one covered research target; an identity-only/name-only row is shown as incomplete and links to create or repair the initial profile. The visible gap count comes from qualitative coverage, so near-total gaps remain diagnosable rather than appearing as a healthy profile.

Deep Research is bounded by tool, search, crawl, evidence-document, and duration budgets. Its tools are read-only: profile/source lookup, provider-routed search, provider-routed page retrieval, and stored-source text search. Activity records intentionally exclude prompts, secrets, raw tool payloads, and hidden reasoning. A saved investigation is not an accepted Company Profile.

Managed AI Research uses an asynchronous Exa Agent run and a durable local job row. The client may leave the workspace while a background worker polls the provider. The adapter sends Exa's required beta request header. A completed result remains investigation material with provider provenance; it is not accepted profile evidence. The optional Maps Embed key must be restricted in Google Cloud to the deployed frontend origins; missing configuration falls back to a normal Google Maps address link.

Ask RAVEN Web Search is enabled per conversation through the Chat capability endpoint. It reuses configured `ISearchProvider` and `ICrawlerProvider` routes, streams sanitized progress over SSE, and persists bounded `ChatWebEvidenceSnapshot` records for cited pages. Permission is optional: profile evidence remains first, and enabling Web Search does not force a provider call on every turn. Web snapshots are Chat evidence only and never become accepted Company Profile evidence automatically.

Only the explicit profile-confirmation action creates a Company Profile version. Deep Research, normal workers, profile generation, navigation restore, and failed/in-progress run recovery never auto-confirm a candidate. A completed ProfileImprovement investigation without a usable Profile v1 is visible as preserved but locked material, is not clickable from the global ready card, and is excluded from Workspace Review.

The Investigation lifecycle and Research Briefings use the `InvestigationLifecycleAndBriefings` EF migration. API startup applies pending migrations. Briefing create/update calls the configured AI provider to synthesize selected persisted Investigations; it does not call Search or Crawl, and tests use a fake provider.

Workspace Review acknowledgement is persisted in SQLite through `WorkspaceResearchReviewState`. The queue groups terminal Native, Deep, and External results by company, method, and normalized topic; acknowledgement is timestamped so newer results reappear. The bulk and smart-cleanup actions only clear review visibility and never delete research history, investigations, sources, or evidence.

## Git workflow

Use `main` plus short-lived feature branches and small pull requests. Coordinate before editing shared contracts, Program.cs, frontend routing/bootstrap, Docker Compose, migrations, or STATUS.md. Do not merge a feature branch until its relevant tests and integration checks are green.
