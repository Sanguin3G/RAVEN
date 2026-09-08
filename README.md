# RAVEN
**Research, Analysis, Verification & Enterprise Navigation**

RAVEN is an AI-assisted Company Intelligence Platform that discovers public company information, builds standardized source-grounded Company Profiles, stores accumulated knowledge, supports AI-assisted research, and tracks company information over time.

The repository currently contains a runnable foundation, not the research product itself. Its first delivery is the evidence-backed Company → Brave → Crawl4AI Local → Gemini → Profile vertical slice.

## Stack

- React + TypeScript + Vite
- ASP.NET Core + Entity Framework Core
- SQLite
- Docker-hosted Crawl4AI Local
- Planned: Brave, Exa, Crawl4AI Cloud, Firecrawl, and Gemini

## Quick start

With .NET 10, Node.js, and Docker Engine (including Docker Engine in WSL) installed:

1. From the repository root: `docker compose up -d crawl4ai`
2. From `backend`: `dotnet restore Raven.sln`, then `dotnet run --project src/Raven.Api`
3. From `frontend`: `npm install`, then `npm run dev`

The API listens on `http://localhost:5180` in development and exposes Company endpoints, `GET /health`, and OpenAPI at `/openapi/v1.json`; the frontend uses port 5173. Provider credentials are not needed for the current backend foundation. Crawl4AI is bound only to `localhost:11235` and uses a development-only token by default; set `CRAWL4AI_API_TOKEN` in a local `.env` before authenticated crawl endpoints are added.

## Documentation

| Topic | Document |
| --- | --- |
| Project scope | [docs/PROJECT.md](docs/PROJECT.md) |
| Architecture | [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) |
| Implementation plan | [docs/PLAN.md](docs/PLAN.md) |
| Current status | [docs/STATUS.md](docs/STATUS.md) |
| Development setup | [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) |
| Codex instructions | [AGENTS.md](AGENTS.md) |
