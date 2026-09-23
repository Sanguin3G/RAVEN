# RAVEN

RAVEN is an AI-assisted Company Intelligence Platform. It turns public-source research into reviewable, evidence-backed Company Profiles that remain useful as research is refreshed.

## Current capabilities

- Company Workspace for company identity, sources, investigations, Briefings, profile history, changes, and monitoring.
- Native RAVEN Research: identity resolution, public-source discovery, source review, acquisition, evidence storage, and human-confirmed profiles.
- Evidence-backed, immutable Company Profile versions with field-level provenance.
- Investigations with explicit origin, purpose, topics, and durable review state across saved and managed research.
- Research Briefings built from selected Investigations, with controlled templates, immutable versions, source snapshots, and reviewable updates.
- Ask RAVEN: profile-grounded conversations with citations and optional, conversation-scoped Web Search.
- Monitoring and targeted profile improvement, both review and confirmation gated.

## Quick start

Install .NET 10, Node.js, Docker Engine, and Git. From the repository root:

```powershell
docker compose up -d crawl4ai
```

Start the API:

```powershell
cd backend
dotnet restore Raven.sln
dotnet run --project src/Raven.Api
```

Start the frontend in another terminal:

```powershell
cd frontend
npm install
npm run dev
```

The API listens on `http://localhost:5180`; the frontend listens on `http://localhost:5173`. Configure credentials through environment variables or user secrets—never commit a populated `.env` file.

## Architecture

RAVEN uses React/Vite, ASP.NET Core minimal APIs, Entity Framework Core, and SQLite. Research, provider access, profile confirmation, investigations, Chat, and monitoring have separate ownership boundaries. Search, crawl, and AI calls use mockable provider abstractions; profile acceptance is always application controlled.

For details, see [Architecture](docs/ARCHITECTURE.md), [Development](docs/DEVELOPMENT.md), [Demo](docs/DEMO.md), [Status](docs/STATUS.md), and [repository instructions](AGENTS.md).
