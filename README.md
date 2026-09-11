# RAVEN

**Research, Analysis, Verification & Enterprise Navigation**

RAVEN is an AI-assisted Company Intelligence Platform. It discovers public company sources, lets a researcher choose evidence to acquire, produces a source-grounded Company Profile, and preserves the accepted profile and its provenance.

## Current milestone

Day 5 extends the working research slice with evidence coverage and workspace quality:

```text
Company identity → duplicate check → optional AI grounding → source review
→ provider-routed acquisition → evidence → Gemini profile candidate
→ human confirmation → versioned profile/history/changes
```

It also adds persistent settings, Exa/Firecrawl provider routing, manual refresh, monitoring that produces review-ready updates, and a bounded Deep Research backend with saved investigations. Day 5 adds coverage-aware research roots, generic corporate-family discovery, bounded official-site expansion, MaSoThue directory parsing, target-scoped profile enrichment, workspace lifecycle safeguards, and a docked Ask RAVEN frontend boundary for Hung's backend.

## Stack

- React + TypeScript + Vite + Phosphor icons
- ASP.NET Core + Entity Framework Core
- SQLite
- Brave and Exa Search
- Crawl4AI Local, Firecrawl, and Exa Contents retrieval
- Gemini structured output
- Microsoft Agent Framework for bounded Deep Research

## Quick start

Install .NET 10, Node.js, Docker Engine, and Git. Start the local crawler from the repository root:

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

The API listens on `http://localhost:5180`; the frontend listens on `http://localhost:5173`. Configure provider credentials outside the repository through environment variables or user secrets. Never commit populated `.env` files.

## Documentation

| Topic | Document |
| --- | --- |
| Product scope | [docs/PROJECT.md](docs/PROJECT.md) |
| Architecture | [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) |
| Milestones | [docs/PLAN.md](docs/PLAN.md) |
| Integrated status | [docs/STATUS.md](docs/STATUS.md) |
| Setup and configuration | [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) |
| Day-5 demonstration flow | [docs/DEMO.md](docs/DEMO.md) |
| Collaboration instructions | [AGENTS.md](AGENTS.md) |
