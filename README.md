# RAVEN

**Research, Analysis, Verification & Enterprise Navigation**

RAVEN is an AI-assisted Company Intelligence Platform. It discovers public company sources, lets a researcher choose evidence to acquire, produces a source-grounded Company Profile, and preserves the accepted profile and its provenance.

## Current milestone

The Day 8 research continuation is integrated on `main` and adds three complementary research strategies while preserving RAVEN's evidence boundary:

- Native Research owns search, acquisition, evidence, and profile verification.
- Managed AI Research launches durable asynchronous Exa Agent investigations.
- External Research Import creates a focused copyable brief for any external assistant and imports pasted Markdown as untrusted research material.
- Workspace Review groups repeated research outcomes into a durable queue with bulk acknowledgement and safe smart cleanup; cleanup never deletes research history.

Deep Research does not block initial profile creation. It may finish before Profile v1 exists, but a completed ProfileImprovement investigation stays muted, locked, and out of Workspace Review until a supported model-created profile exists. Native RAVEN Research remains the fast integrated path; External AI Assist remains unverified review material. Profile creation is user-confirmed only: an LLM, worker, failed run, or navigation restore cannot create a profile on its own. Completed Investigations can be explicitly attached to and removed from a conversation. Actual LLM grounding of attached Investigations remains Hung's Chat-backend integration seam; the implementation does not create a parallel Chat agent.

Day 7 closes execution-efficiency work and establishes the Ask RAVEN Chat foundation:

```text
Identity preflight → duplicate check → one discovery phase → source review
→ selected-source acquisition → evidence → Gemini profile candidate
→ human confirmation → versioned profile/history/changes
```

Identity preflight resolves explicit identifiers locally or asks one bounded topology question before public-source research. The obsolete active corporate-family search/grounding expansion is retired: a resolved or selected identity starts one normal discovery phase.

Developer execution telemetry is sanitized, bounded, and batch-persisted outside provider-call latency; durable user-visible activity, including Deep Research activity, remains separate. Ask RAVEN is persistent profile-grounded Chat: factual company answers require cited evidence, while greetings and product guidance can respond naturally. Deep Research remains an explicit workflow, and web-enabled Chat is intentionally deferred.

Navigation restore distinguishes a missing/stale run from a failed rehydration request. A known run is preserved with a targeted restore error and retry action rather than being relabeled as a generic RAVEN server failure. Identity-only or name-only profile rows left by an interrupted run are treated as incomplete; supported coverage gaps and model provenance must be present before Profile Improvement is enabled.

## Stack

- React + TypeScript + Vite + Phosphor icons
- ASP.NET Core + Entity Framework Core
- SQLite
- Brave and Exa Search
- Crawl4AI Local and Exa Contents retrieval
- Exa Agent for managed asynchronous research
- Gemini structured output
- Provider-neutral external research import

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
