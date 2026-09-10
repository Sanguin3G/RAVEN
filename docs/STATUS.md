# RAVEN Status
Last updated: 2026-09-10
Current milestone: M1 — Core Research

## Current Goal

Day 3 integrated vertical slice: rich Company identity and duplicate checks; staged Brave discovery, human source selection, Crawl4AI acquisition, source taxonomy/classification, evidence persistence, Gemini structured profile candidates, deterministic evidence validation, human confirmation, immutable profile versions, and research-event logging. RAG, real Ask RAVEN answers, Deep Research, MCP, Exa, and Firecrawl remain out of scope.

Turn the Day 1 Company and Crawl4AI foundation into the first evidence-backed Company → Brave → Crawl4AI Local → Gemini → Profile vertical slice. No search, crawling, AI, source-persistence, or research workflow is implemented yet.

## Person A

Now:
- Day 1 backend foundation is implemented: SQLite migration, Company API, CORS, backend integration tests, and a resilient Crawl4AI Local status probe.

Next:
- Implement the first deterministic research vertical slice, beginning with provider contracts and Brave search.

Blocked:
- Provider credentials and implementations are not configured. Crawl4AI Local is running and reachable; it is not yet wired as a crawling provider.

## Person B

Now:
- Company creation, list, and detail UI is implemented on `main` and targets the Day 1 Company API contract.

Next:
- Integrate against the merged backend and add research progress/profile display as the API grows.

Blocked:
- No current API-contract blocker.

## Integrated

- [x] Repository root, environment template, and Crawl4AI Local Docker Compose service
- [x] ASP.NET Core API startup with migrations, development CORS, GET /api, and GET /health
- [x] SQLite Company entity, `InitialCreate` migration, and database-on-startup migration application
- [x] Company API: `POST /api/companies`, `GET /api/companies`, and `GET /api/companies/{id}`
- [x] Backend API integration tests for creation, listing, retrieval, validation, and not-found behavior
- [x] React company creation, list, and detail flow
- [x] Crawl4AI Local availability probe at `GET /api/system/crawler-status`; Docker setup uses localhost-only port exposure
- [x] Brave search, Crawl4AI acquisition, source persistence, staged research, source review, Gemini profile candidates, profile confirmation, and research-event logging
- [ ] RAG, Ask RAVEN answers, Deep Research, MCP, Exa, Firecrawl, and scheduled tracking

## Provider Status

| Capability | Provider | Status |
| --- | --- | --- |
| Search | Brave | Implemented; configured only when a server-side key is present |
| Search | Exa | Not started |
| Search | Crawl4AI Cloud | Not started |
| Search | Firecrawl | Not started |
| Crawl | Crawl4AI Local | Docker and availability probe implemented and verified against local image 0.9.3 |
| Crawl | Crawl4AI Cloud | Not started |
| Crawl | Firecrawl | Not started |
| Fast AI | Gemini Flash-Lite | Implemented as a mockable structured-output adapter; live key not verified in this change |
| Deep AI | Gemini Flash | Configuration only; Deep Research is not implemented |
| MCP | Exa | Not started |
| MCP | Firecrawl | Not started |

## Blockers / Decisions

- `dotnet build Raven.sln` and `dotnet test Raven.sln` pass locally (4 backend integration tests).
- Crawl4AI Local is not yet a crawler provider; the current endpoint only checks service availability and handles a stopped service without failing API startup.
- The repository defines provider-key placeholders in `.env.example`, but no search, crawler, or AI provider adapter exists yet.
- Current Crawl4AI images need `CRAWL4AI_API_TOKEN` to accept host traffic. Compose supplies a development-only default and binds port 11235 to localhost.
- Keep external calls mockable and record requested versus actual provider whenever provider work begins.

## Next Integration Point

Implement provider contracts and the Brave → Crawl4AI Local → source-persistence path. Preserve the established Company DTO contract unless frontend and backend coordinate a change. Detailed tasks belong in GitHub Issues.
