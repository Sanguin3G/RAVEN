# RAVEN Status

Last updated: 2026-09-19

Research workspace: **implemented on `main`**. Investigations are durable research workspaces with shared Native/Deep/External origins, versioned derived organization, preserved raw material, claims, source leads, uncertainties, and follow-up gaps. External Research Assist is a focused provider-neutral copy/paste flow with durable asynchronous analysis, minimize/reopen behavior, AI-assisted and human review, unverified provenance, and Save to Investigation. Provider citations remain visible context; External Assist does not re-search or re-crawl them. One global research activity surface distinguishes Native, Deep, and External work and running versus ready-for-review state. Managed AI Research settings expose provider-neutral depth and map to Exa effort internally. Ready investigation material can enter the existing server-owned targeted profile-patch review without a second search/crawl; confirmation still creates a new immutable profile version. Deep ProfileImprovement results remain locked until a supported model-created profile exists, and only explicit user confirmation can create a profile. The implementation does not create a parallel Chat grounding path.

Day 8 managed research and provider consolidation: **PASS — integrated into `main`**. Managed AI Research creates durable asynchronous Exa Agent jobs, polls through an isolated background worker, normalizes results with claims, cited source leads, and uncertainties, and surfaces completion without blocking Ask RAVEN. External Research Import builds copyable provider-neutral briefs and saves pasted Markdown as untrusted review material. Firecrawl is no longer an active route, settings choice, or provider-status surface; legacy priorities normalize safely. Company Overview provides an optional Google Maps Embed adapter and external-map fallback. Investigation attachments are durable/removable per conversation; actual LLM grounding remains Hung's Ask RAVEN context-extension seam. Current Exa Agent compatibility follows the official `/agent/runs` Bearer-authenticated contract; no live validation was possible without `EXA_API_KEY`.

Day 7 execution efficiency: **PASS — integrated into `main`**. Execution telemetry is bounded, buffered, and batch-persisted through isolated EF scopes; Deep Research activity no longer allocates sequences through `MAX(sequence)`; new discovery no longer invokes the legacy family-search/grounding expansion. Ask RAVEN's merged profile-chat backend is integrated: conversations, messages, citations, and the bounded evidence-excerpt tool are implemented. Greetings and application guidance are citation-free while factual company answers remain citation-gated. Web-enabled Chat remains future work; the composer offers a truthful disabled Web Search slot and a real Investigations handoff.

Day-6 stabilization: **PASS — integrated into `main`**. Restored browser research state now accepts only active, known workflow stages; cancelled, terminal, missing, and legacy run sessions reset to a blank research form. User-confirmed company merges now preserve and hydrate combined profile history in confirmation-time order, retain compatible dependent evidence/workspace records, fill only missing stable canonical identity fields, and route to the canonical dossier. Company List actions now open Monitoring or targeted profile improvement directly; companies without an accepted profile receive refresh/review guidance.
Day 6 identity preflight: **PASS — integrated into `main`** from `feat/day6-identity-resolution` on 2026-09-13. The controlled live Gemini Prep v2 asks the model only for identity topology (`SpecificEntity`, `CorporateFamilyShorthand`, `NameCollision`, or `Unknown`); deterministic RAVEN policy derives workflow state. The probe classified FPT, Viettel, and Vingroup as family shorthand, specific company inputs as specific entities, and the deliberately obscure input as unknown. The initial workflow resolves identity before duplicate matching, Company creation, and discovery; resolved identity snapshots persist with the run. Explicit identifiers and accepted targeted-enrichment identity bypass identity AI. Ambiguous and collision results use a parent-first/choice workflow, while guided refinement is a dismissible modal that requests useful additional hints without creating a second search form. Day 7 removed the old active candidate-driven family-search expansion while retaining historical read compatibility.
Day 5.5 closure: **PASS** on `feat/day5-enrichment-workspace`. Execution telemetry, Resilient preset compatibility, run-scoped settings snapshots, workflow extraction, and Microsoft Edge smoke checks are complete. CSS ownership splitting remains intentionally deferred to avoid expanding the Day-6 preparation scope.
Day-5 scope is preserved in the branch history: coverage-aware selection, corporate-family discovery, official-domain evidence planning, MaSoThue parsing, target-scoped profile patching, cancellable background research, workspace health, lifecycle operations, and corrected workspace IA are included in the Day-6 release.
Current branch: **main** (research-workspace continuation integrated)
Current milestone: **M2 — Research Intelligence and Tracking**

## Current state

`main` contains the integrated research-workspace continuation. The product path is stable around user-confirmed profile creation, supported-profile readiness checks, and targeted recovery for known research runs.

## Current continuation notes

- Deep `ProfileImprovement` results can finish before Profile v1, but remain visibly locked and excluded from Workspace Review until a supported model-created profile exists.
- Profile confirmation is the only profile-creation boundary. A failed, cancelled, or still-running RAVEN run cannot create a version; generated candidates require model provenance, supported target coverage, and source-backed evidence before confirmation.
- Identity-only/name-only profile rows are preserved as incomplete artifacts. They do not unlock Deep review or targeted Profile Improvement, and the Overview keeps the qualitative gap count visible so near-total missing coverage is diagnosable.
- Navigation restore distinguishes stale missing state from a failed related request: known runs stay preserved with a targeted retry message instead of becoming the generic API-unavailable failure.
- Workspace Review now stores durable acknowledgement watermarks in SQLite, groups repeated terminal research by company/method/topic, keeps reviewed results hidden until newer activity appears, and offers bulk acknowledgement plus confirmation-protected smart cleanup without deleting research.

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
- Pre-search identity resolution with explicit-identifier fast paths, topology-only model assistance, deterministic status policy, clarification/manual exact-name paths, and bounded resolved-identity run snapshots.
- AI semantic source reranking with short user-facing relevance reasons; deterministic classification remains the fallback.
- Persistent research settings for grounding, profile/deep model roles, reranking, and provider priorities/presets.
- Manual research refresh, immutable profile history, deterministic ProfileChange records, and the Changes workspace.
- Exa Search and Exa Contents retrieval through neutral provider interfaces and retryable-only fallback routing.
- Company monitoring settings and one in-process worker. Scheduled research creates a profile candidate ready for human review; it never confirms a profile automatically.
- Bounded Microsoft Agent Framework Deep Research runs with read-only tools, persisted safe activity, and saved research artifacts that cannot mutate a Company Profile.
- Coverage levels and ResearchTargets drive bounded follow-up research. Recommended roots remain complementary; one approved official root may yield several bounded same-domain documents.
- Corporate-family discovery, coverage-aware source selection, target-aware official-site planning, and MaSoThue parsing as a BusinessDirectory rather than an official registry.
- Targeted enrichment and server-owned ProfilePatch confirmation preserve every unrelated accepted profile field.
- Background research start, cancellation, and active-run listing are available through an in-process queue; queued work is deliberately not durable across an API restart.
- The latest server-owned profile candidate is available by research run for review without treating it as accepted profile truth.
- `CompanyProfileReadiness` classifies identity-only, sparse, partial, and complete snapshots for improvement gating; `ManagedResearchPurpose` persists whether a managed job is General or ProfileImprovement.
- Company health, deterministic duplicate review, archive/restore, permanent-delete confirmation, and transactional merge preview/confirmation.
- Company workspace tabs are Overview, Sources, Investigations, Changes, and Monitoring. Ask RAVEN is a responsive profile-grounded Chat dock using Hung's integrated backend contract.
- Managed AI Research, External Research Assist, durable Investigation organization revisions, durable analysis jobs, one shared research activity surface, completion notification polling, explicit conversation attachments, and optional map adapter/fallback.
- Target-specific External AI Assist briefs for all selected improvement areas, plus compact durable research review grouping and acknowledgement endpoints.

## Verified checks

Latest local integration check:

```text
dotnet build Raven.sln --no-restore   passed
dotnet test Raven.sln --no-build --no-restore    315 passed
npx tsc --noEmit                                 passed
npx vitest run (focused workspace files)         13 passed across 5 files
npm run build                                    passed
```

Microsoft Edge/Playwright runs through the explicit `msedge` project/channel verified the Day 7 flows, Day 8 managed-research continuation, Day 9 Investigation workspace, External Research Assist copy/paste/analyze/minimize/reopen/review/save/verify flow, and 390×844 mobile workspace/modal screenshots. The API smoke test applied all migrations, including the organization, settings, and analysis-job migrations, to a clean temporary SQLite database and returned 200 for `/health`, OpenAPI, and managed-research settings. No paid provider call was made by the deterministic suite.

The legacy Day-5 candidate-driven family vocabulary remains readable for historical compatibility but is not used by the new initial identity preflight. Day 7 removed its active downstream family-search expansion.

## Provider status

| Capability | Provider | Implementation state |
| --- | --- | --- |
| Search | Brave | Implemented; requires server-side key to run live searches |
| Crawl | Crawl4AI Local | Implemented; requires reachable local Docker service and matching token |
| Fast AI | Gemini | Implemented structured-output adapter; requires server-side key |
| Deep AI | Gemini + Microsoft Agent Framework | Bounded Deep Research backend; needs Gemini to run live |
| Search | Exa | Implemented; requires `EXA_API_KEY` |
| Crawl | Exa Contents | Implemented; requires `EXA_API_KEY` |
| Managed AI Research | Exa Agent | Durable async jobs; requires `EXA_API_KEY`; provider-neutral product depth maps to Exa effort |
| External AI | Manual provider-neutral import | Copyable brief and pasted Markdown; never accepted evidence automatically |
| Maps | Google Maps Embed (optional) | Adapter with external-map fallback |

`GET /api/system/provider-status` reports safe configured/available state. It never exposes credentials. Research settings persist only safe model/provider preferences and support the approved `gemini-3.5-flash-lite` and `gemini-3.8-flash` choices.

## Deliberately not implemented

- Source chunks, embeddings, vector retrieval, and RAG.
- MCP, Crawl4AI Cloud, full monitoring operations, and arbitrary profile-version comparisons.
- Web-enabled normal Chat and actual Search-the-web capability remain Hung-owned.
- Hung's supported prompt-context extension for explicit Investigation attachments.

## Next integration point

Preserve the integrated Ask RAVEN contract: Company ID, SourceDocument/SourceKind, CompanyProfileVersion/ProfileEvidence, DeepResearchRun activity, SavedResearchArtifact, and Company workspace boundaries.
