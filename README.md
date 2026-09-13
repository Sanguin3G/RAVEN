# RAVEN

**Research, Analysis, Verification & Enterprise Navigation**

RAVEN is an AI-assisted Company Intelligence Platform. It discovers public company sources, lets a researcher choose evidence to acquire, produces a source-grounded Company Profile, and preserves the accepted profile and its provenance.

## Current milestone

Day 6 adds identity resolution and stabilizes the working research slice:

```text
Company identity → duplicate check → optional AI grounding → source review
→ provider-routed acquisition → evidence → Gemini profile candidate
→ human confirmation → versioned profile/history/changes
```

Identity preflight resolves explicit identifiers locally or asks one bounded topology question before public-source research. Day 6 also hardens cancellable research restoration, profile-preserving duplicate merges, direct Monitoring/enrichment workspace actions, and sparse-profile guidance. RAVEN keeps unsupported facts unknown and preserves accepted profile history.

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
| Customer / mentor demonstration flow | [docs/DEMO.md](docs/DEMO.md) |
| Collaboration instructions | [AGENTS.md](AGENTS.md) |
