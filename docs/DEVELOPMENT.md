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

The model-preference endpoint permits only `gemini-3.5-flash-lite` and `gemini-3.8-flash`. It holds the local runtime choice until the API restarts; it does not write configuration or accept credentials.

## Configuration and secrets

Never commit a populated `.env` file. RAVEN does not load `.env` automatically; export values into the API process or use user secrets.

| Capability | Environment variables |
| --- | --- |
| Brave | `BRAVE_SEARCH_API_KEY` |
| Exa Search and Contents | `EXA_API_KEY` |
| Firecrawl Search and Crawl | `FIRECRAWL_API_KEY` |
| Crawl4AI Local | `CRAWL4AI_LOCAL_BASE_URL`, `CRAWL4AI_API_TOKEN` |
| Gemini | `GEMINI_API_KEY`, optional `GEMINI_FAST_MODEL`, `GEMINI_DEEP_MODEL` |
| SQLite | `ConnectionStrings__Raven` |

The equivalent nested configuration sections remain available for local configuration. Provider keys are server-only and must never be returned to React, written to ResearchEvents, or added to source control.

Research Settings persist safe model roles, grounding/reranking preferences, and provider priorities in SQLite. They never persist provider keys. `RAVEN Local First` uses Brave plus Crawl4AI Local; Resilient and Cloud presets can route retrieval through Exa Contents and Firecrawl after retryable failures. Authentication, configuration, and invalid-request errors never silently fall back.

## Docker

`docker-compose.yml` runs `unclecode/crawl4ai:latest` and exposes its port locally. Current Crawl4AI images need `CRAWL4AI_API_TOKEN` for host traffic. Give the API the same token and keep production values distinct from development values.

```powershell
docker compose logs crawl4ai
```

Do not silently substitute a cloud crawler if the local crawler fails; surface the source-level failure and continue eligible work.

## Database

SQLite is the source of record. API startup applies EF Core migrations. With the documented backend startup command, the default file is `backend/src/Raven.Api/raven.db`.

```powershell
# from backend
dotnet ef migrations add <MigrationName> --project src/Raven.Api --startup-project src/Raven.Api
dotnet ef database update --project src/Raven.Api --startup-project src/Raven.Api
```

## Tests and checks

```powershell
# from backend
dotnet build Raven.sln
dotnet test Raven.sln

# from frontend
npm test -- --run
npm run build
```

Provider tests must use fakes, mocks, or fixtures. Normal automated tests must not require live provider credentials, paid traffic, TopCV availability, LinkedIn access, or a running crawler.

Microsoft Edge completion checks use Playwright with `--browser msedge`; Chromium is not a substitute. The repository does not currently ship live-provider fixtures, so external-provider checks must be separately marked as controlled live smoke tests.

## Monitoring and Deep Research

Monitoring and Deep Research use in-process `BackgroundService` workers. They execute only while the API process is running; this project deliberately does not add an external scheduler or job broker. Monitoring produces a review-ready profile candidate and never accepts a profile automatically.

The background research start endpoint also uses an in-process channel-backed worker. It returns `202 Accepted`, exposes active runs, and supports cancellation. It is not a durable job queue: a process restart drops queued work, by design for this MVP.

Deep Research is bounded by tool, search, crawl, evidence-document, and duration budgets. Its tools are read-only: profile/source lookup, provider-routed search, provider-routed page retrieval, and stored-source text search. Activity records intentionally exclude prompts, secrets, raw tool payloads, and hidden reasoning. A saved investigation is not an accepted Company Profile.

## Git workflow

Use `main` plus short-lived feature branches and small pull requests. Coordinate before editing shared contracts, Program.cs, frontend routing/bootstrap, Docker Compose, migrations, or STATUS.md. Do not merge a feature branch until its relevant tests and integration checks are green.
