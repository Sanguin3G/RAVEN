# RAVEN Status

Last updated: 2026-09-10
Current milestone: **M1 — Core Research**

## Current state

The Day-3 Company Intelligence vertical slice is integrated in this release change set. A researcher can identify or reuse a company, discover candidates, select sources, acquire evidence, generate an evidence-grounded profile candidate, and explicitly confirm an immutable profile version.

## Integrated

- SQLite-backed Company API with rich identity fields and duplicate suggestions.
- Deterministic staged `ResearchRun` lifecycle: discovery, source selection, acquisition, evidence-ready, profile generation, confirmation, completion, and failure.
- Brave search with bounded query planning, URL normalization, candidate persistence, recommendation reasons, and official-domain prioritization.
- Source taxonomy and presentation for official websites/documents, business registries, TopCV, LinkedIn, news, external websites, and search results.
- Bounded same-domain official-site discovery and opportunistic TopCV parsing.
- Crawl4AI Local acquisition, SourceDocument persistence, content de-duplication, truthful crawl/document counters, and partial-failure continuation.
- Gemini structured-output provider behind `IAiModelProvider`; bounded profile input; deterministic profile/evidence validation; human confirmation; CompanyProfileVersion and ProfileEvidence persistence.
- Safe ResearchEvent logging for search, crawl, parsing, profile, and confirmation activity.
- React staged research workspace, Company dossier, source/evidence cards, fixed responsive sidebar, custom RAVEN logo/favicon, Phosphor core icons, status route, and Settings model controls.

## Verified checks

Latest local integration check:

```text
dotnet test Raven.sln --no-restore    100 passed
npm test -- --run                     22 passed
npm run build                         passed
```

Edge/Playwright was used to verify the desktop shell, collapsed sidebar, account/appearance panel, mobile drawer and Escape dismissal, System Status, and live Settings model updates. Live discovery and acquisition were previously exercised with well-known Vietnamese companies; one Crawl4AI timeout surfaced as a truthful per-source failure without failing the run.

## Provider status

| Capability | Provider | Implementation state |
| --- | --- | --- |
| Search | Brave | Implemented; requires server-side key to run live searches |
| Crawl | Crawl4AI Local | Implemented; requires reachable local Docker service and matching token |
| Fast AI | Gemini | Implemented structured-output adapter; requires server-side key |
| Deep AI | Gemini | Model preference/configuration only; Deep Research is not implemented |
| Search/Crawl | Exa, Firecrawl, Crawl4AI Cloud | Future |
| MCP | Exa, Firecrawl | Future |

`GET /api/system/provider-status` reports safe configured/available state. It never exposes credentials. Settings supports only the approved `gemini-3.5-flash-lite` and `gemini-3.8-flash` model choices, held for the local API runtime until restart.

## Deliberately not implemented

- Source chunks, embeddings, vector retrieval, and RAG.
- Real Ask RAVEN answers, conversations, or citation answering.
- Deep Research, Agent Framework, MCP, Exa, Firecrawl, scheduled monitoring, and change detection.
- Full profile-version history and field editing UI.

## Next integration point

Stabilize the M1 experience, then introduce source chunking and company-filtered retrieval before building real Ask RAVEN answers. Preserve the existing Company, SourceDocument, SourceKind, CompanyProfileVersion, ProfileEvidence, and Company workspace boundaries.
