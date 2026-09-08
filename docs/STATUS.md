# RAVEN Status
Last updated: 2026-09-08
Current milestone: M1 — Core Research

## Current Goal

Turn the existing runnable foundation into the first evidence-backed Company → Brave → Crawl4AI Local → Gemini → Profile vertical slice. No provider integration or research workflow is currently implemented.

## Person A

Now:
- No active issue recorded in the repository.

Next:
- Establish Company API, migrations, and provider/research contracts.

Blocked:
- Provider credentials and provider implementations are not configured.

## Person B

Now:
- No active issue recorded in the repository.

Next:
- Replace scaffolded Company routes with the create/list and research-flow UI once API contracts exist.

Blocked:
- Product API contracts are not yet implemented.

## Integrated

- [x] Repository root, environment template, and Crawl4AI Local Docker Compose service
- [x] ASP.NET Core API startup with SQLite initialization, CORS, GET /api, and GET /health
- [x] Initial EF Core Company entity and RavenDbContext
- [x] React, TypeScript, and Vite shell with placeholder routes
- [ ] Company CRUD API and UI
- [ ] Search, crawl, AI, research, RAG, Deep Research, and tracking workflows

## Provider Status

| Capability | Provider | Status |
| --- | --- | --- |
| Search | Brave | Not started |
| Search | Exa | Not started |
| Search | Crawl4AI Cloud | Not started |
| Search | Firecrawl | Not started |
| Crawl | Crawl4AI Local | In progress |
| Crawl | Crawl4AI Cloud | Not started |
| Crawl | Firecrawl | Not started |
| Fast AI | Gemini Flash-Lite | Not started |
| Deep AI | Gemini Flash | Not started |
| MCP | Exa | Not started |
| MCP | Firecrawl | Not started |

## Blockers / Decisions

- Packages have not been restored in this checkout; no runtime or build verification was recorded during foundation setup.
- Crawl4AI Local is defined in Docker Compose, but no crawler-provider integration has been implemented.
- The repository defines provider-key placeholders in .env.example, but no provider adapter or credential loading path is implemented yet.
- Keep external calls mockable and record requested versus actual provider whenever provider work begins.

## Next Integration Point

Agree the Company DTO/API contract and implement the narrow foundation needed for the M1 vertical slice. Detailed tasks belong in GitHub Issues.
