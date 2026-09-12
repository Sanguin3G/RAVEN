# RAVEN Status

Last updated: 2026-09-12
Day 6 identity preflight: **NO-GO / PARTIAL** on `feat/day6-identity-resolution`. A controlled, identity-only live Gemini API spike completed through RAVEN's real `IAiModelProvider`/`GeminiProvider` path using `gemini-3.5-flash-lite`; it parsed all 12 structured responses and handled an obscure input as `Unknown`. However, generic `FPT` and `Viettel` were auto-resolved to parent groups rather than returned as family choices, and `Vingroup` returned the contradictory combination `Resolved` plus `CorporateFamily`. That is insufficiently humble for the required pre-search gate, so no Day-6 production identity-resolution path, schema migration, or frontend workflow has been installed. The bounded probe remains available for prompt/model reevaluation; Day-5.5 behavior is intentionally unchanged.
Day 5.5 closure: **PASS** on `feat/day5-enrichment-workspace`. Execution telemetry, Resilient preset compatibility, run-scoped settings snapshots, workflow extraction, and Microsoft Edge smoke checks are complete. CSS ownership splitting remains intentionally deferred to avoid expanding the Day-6 preparation scope.
Day-5 branch: coverage-aware selection, corporate-family discovery, official-domain evidence planning, MaSoThue parsing, target-scoped profile patching, cancellable background research, workspace health, lifecycle operations, and corrected workspace IA are integrated here but not merged into `main`.
Current branch: **feat/day6-identity-resolution** (NO-GO experiment; not merged into `main`)
Current milestone: **M2 — Research Intelligence and Tracking**

## Current state

The Day-3 Company Intelligence vertical slice remains intact. Day 4 adds AI-assisted identity resolution and source relevance, persistent research settings, refresh/history/change tracking, provider routing, monitoring, bounded Deep Research, and saved investigation persistence. Day 5 adds coverage-driven follow-up research, generic corporate-family planning with a cautious deterministic fallback, deliberate MaSoThue directory evidence, target-scoped profile patches, cancellable in-process background discovery, lifecycle safety, and the clarified Company workspace.

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
- Coverage levels and ResearchTargets drive bounded follow-up research. Recommended roots remain complementary; one approved official root may yield several bounded same-domain documents.
- Corporate-family discovery, coverage-aware source selection, target-aware official-site planning, and MaSoThue parsing as a BusinessDirectory rather than an official registry.
- Targeted enrichment and server-owned ProfilePatch confirmation preserve every unrelated accepted profile field.
- Background research start, cancellation, and active-run listing are available through an in-process queue; queued work is deliberately not durable across an API restart.
- The latest server-owned profile candidate is available by research run for review without treating it as accepted profile truth.
- Company health, deterministic duplicate review, archive/restore, permanent-delete confirmation, and transactional merge preview/confirmation.
- Company workspace tabs are Overview, Sources, Investigations, Changes, and Monitoring. Ask RAVEN is a responsive right dock that remains an integration-pending frontend boundary until Hung's backend contract is published.

## Verified checks

Latest local integration check:

```text
dotnet build Raven.sln --no-restore   passed
dotnet test Raven.sln --no-restore    257 passed
npm run build                         passed
Vitest focused suites                  42 passed across isolated single-worker runs
npm run build                         passed
```

Microsoft Edge/Playwright verified the desktop Settings provider presets and persistence, collapsed sidebar, 390×844 mobile drawer and Escape dismissal. The API smoke test applied all migrations to a temporary SQLite database, returned 200 for `/health` and OpenAPI, and correctly rejected an unknown-company Deep Research request. No paid provider call was made. Live discovery and acquisition were previously exercised with well-known Vietnamese companies; one Crawl4AI timeout surfaced as a truthful per-source failure without failing the run.

Day-5 browser coverage currently covers Company List responsive behavior only. Provider-dependent FPT-family, MaSoThue, and targeted-enrichment scenarios require deterministic fixtures before they can be reported as Edge passes.

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
- MCP, Crawl4AI Cloud, full monitoring operations, and arbitrary profile-version comparisons.
- Hung's Ask RAVEN backend, conversation persistence, Quick Ask orchestration, and prompt/context contract. The Day-5 dock deliberately does not fabricate messages while that handoff is unavailable.

## Next integration point

Consume Hung's actual Ask RAVEN backend contract through the frontend adapter boundary when it is published. Preserve Company ID, SourceDocument/SourceKind, CompanyProfileVersion/ProfileEvidence, DeepResearchRun activity, SavedResearchArtifact, and Company workspace boundaries.
