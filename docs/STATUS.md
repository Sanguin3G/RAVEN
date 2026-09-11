# RAVEN Status

Last updated: 2026-09-11
Current branch: **feat/day4-research-intelligence** (not merged into `main`)
Current milestone: **M2 — Research Intelligence and Tracking**

## Current state

The Day-3 Company Intelligence vertical slice remains intact. This unmerged Day-4 branch adds AI-assisted identity resolution and source relevance, persistent research settings, refresh/history/change tracking, provider routing, monitoring, bounded Deep Research, and saved investigation persistence.

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
- AI identity grounding with Auto/Always/Off modes, persisted grounded identity candidates, user target selection, and deterministic fallback.
- AI semantic source reranking with short user-facing relevance reasons; deterministic classification remains the fallback.
- Persistent research settings for grounding, profile/deep model roles, reranking, and provider priorities/presets.
- Manual research refresh, immutable profile history, deterministic ProfileChange records, and the Changes workspace.
- Exa Search, Firecrawl Search/Crawl, and Exa Contents retrieval through neutral provider interfaces and retryable-only fallback routing.
- Company monitoring settings and one in-process worker. Scheduled research creates a profile candidate ready for human review; it never confirms a profile automatically.
- Bounded Microsoft Agent Framework Deep Research runs with read-only tools, persisted safe activity, and saved research artifacts that cannot mutate a Company Profile.

## Verified checks

Latest local integration check:

```text
dotnet test Raven.sln --no-build      197 passed
npm test -- --run                     33 passed
npm run build                         passed
```

Microsoft Edge/Playwright verified the desktop Settings provider presets and persistence, collapsed sidebar, 390×844 mobile drawer and Escape dismissal. The API smoke test applied all migrations to a temporary SQLite database, returned 200 for `/health` and OpenAPI, and correctly rejected an unknown-company Deep Research request. No paid provider call was made. Live discovery and acquisition were previously exercised with well-known Vietnamese companies; one Crawl4AI timeout surfaced as a truthful per-source failure without failing the run.

## Provider status

| Capability | Provider | Implementation state |
| --- | --- | --- |
| Search | Brave | Implemented; requires server-side key to run live searches |
| Crawl | Crawl4AI Local | Implemented; requires reachable local Docker service and matching token |
| Fast AI | Gemini | Implemented structured-output adapter; requires server-side key |
| Deep AI | Gemini + Microsoft Agent Framework | Bounded Deep Research backend; needs Gemini to run live |
| Search | Exa | Implemented; requires `EXA_API_KEY` |
| Crawl | Exa Contents | Implemented; requires `EXA_API_KEY` |
| Search/Crawl | Firecrawl | Implemented; requires `FIRECRAWL_API_KEY` |
| MCP | Exa, Firecrawl | Future |

`GET /api/system/provider-status` reports safe configured/available state. It never exposes credentials. Research settings persist only safe model/provider preferences and support the approved `gemini-3.5-flash-lite` and `gemini-3.8-flash` choices.

## Deliberately not implemented

- Source chunks, embeddings, vector retrieval, and RAG.
- RAG, SourceChunk/embeddings, real Ask RAVEN answers, and conversation persistence/UI.
- MCP, Crawl4AI Cloud, full monitoring operations, and arbitrary profile-version comparisons.
- Deep Research cancellation and a user-facing assistant panel; Hung's UI can consume the stable backend contract.

## Next integration point

Keep the Day-4 branch separate until review, then integrate the backend handoff for Hung's company-scoped assistant. Preserve Company ID, SourceDocument/SourceKind, CompanyProfileVersion/ProfileEvidence, DeepResearchRun activity, SavedResearchArtifact, and Company workspace boundaries.
