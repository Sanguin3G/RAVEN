# RAVEN

Research, Analysis, Verification & Enterprise Navigation is a source-grounded company intelligence platform.

## Status

The repository is initialized with the Phase Zero control plane and a runnable foundation for the API, web client, SQLite, and Crawl4AI container. The first product milestone is the sacred vertical slice: **Company → Brave → Crawl4AI Local → Gemini → Profile**.

## Quick start

1. Copy `.env.example` to `.env` and configure the providers you intend to use.
2. Start Crawl4AI with `docker compose up -d crawl4ai`.
3. From `backend`: `dotnet restore Raven.sln`, then `dotnet run --project src/Raven.Api`.
4. From `frontend`: `npm install`, then `npm run dev`.

See `docs/RUNBOOK.md` for configuration, `docs/ROADMAP.md` for planned work, and `docs/STATUS.md` for the auditable project state.
