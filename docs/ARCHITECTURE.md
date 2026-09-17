# RAVEN Architecture

## Current implementation boundary

RAVEN is a modular ASP.NET Core API with a React/Vite client and SQLite as the system of record. Fast Research remains deterministic and staged. Small in-process workers support research and monitoring; RAG, MCP, distributed queues, and microservices are not implemented.

```text
Identity preflight
→ resolved identity snapshot
→ deterministic discovery
→ persisted ResearchCandidate review
→ selected-source acquisition
→ SourceDocument evidence
→ Gemini structured profile candidate
→ deterministic evidence validation
→ human confirmation
→ immutable CompanyProfileVersion + ProfileEvidence
```

Each network step remains an ordinary HTTP operation. `ResearchRun.Stage`, status, counters, and ResearchEvent records provide truthful UI activity without invented percentage progress. Developer execution telemetry is a separate, bounded diagnostic stream: sanitized Search/Crawl/AI rows enqueue without blocking the caller and a background service persists batches using a fresh EF scope. A terminal run boundary requests a best-effort flush so execution summaries are promptly available. Business state is never dropped. An in-process channel-backed worker may run discovery after a `202 Accepted` start request; it supports cancellation and active-run visibility, but is intentionally non-durable, so queued work does not survive an API restart. Client session restoration is intentionally narrower: only known active/user-actionable stages are restored. Missing, cancelled, completed, failed, or legacy-unrestorable runs are cleared back to a blank research form rather than rendered as a stale company error.

## Technology stack

```text
Frontend        React + TypeScript + Vite + Phosphor icons
Backend         ASP.NET Core minimal APIs
Database        SQLite + Entity Framework Core
Search          Brave Search
Crawler         Crawl4AI Local (Docker)
AI              Gemini structured output
```

## Provider boundaries

Search, crawling, and AI inference are independent capabilities:

```text
ISearchProvider       → BraveSearchProvider
                      → ExaSearchProvider / FirecrawlSearchProvider
ICrawlerProvider      → Crawl4AiLocalProvider / ExaCrawlerProvider / FirecrawlCrawlerProvider
IAiModelProvider      → GeminiProvider
```

Application workflows depend on those neutral capabilities rather than provider-specific DTOs. External calls are mockable in tests. Configuration and status endpoints expose only configured/available/model state; they never return credentials.

Provider routing uses persisted priorities and falls back only after retryable rate-limit, timeout, unavailable, or retrieval failures. Authentication, configuration, invalid requests, and malformed responses remain visible failures. Brave/Exa discover candidate URLs; Crawl4AI Local, Exa Contents, and Firecrawl read selected pages. SQLite preserves evidence. Gemini normalizes bounded evidence into a profile candidate. The application validates and persists accepted facts.

## Identity preflight and tracking

External identity resolution, internal duplicate matching, and source relevance are distinct concerns. `POST /api/research/identity/resolve` receives identity hints only; it creates no Company, `ResearchRun`, evidence, Search, or Crawl work. Strong explicit domains and appropriately scoped registration identifiers resolve deterministically without AI. Otherwise `IIdentityKnowledgeResolver` makes one bounded Gemini topology call. Gemini may describe `SpecificEntity`, `CorporateFamilyShorthand`, `NameCollision`, or `Unknown`, but deterministic RAVEN policy derives the workflow state. In particular, corporate-family shorthand is always ambiguous; the model cannot auto-select the parent as the user's intent.

The resolved target is carried into discovery as a bounded `ResolvedIdentitySnapshot` persisted with the `ResearchRun`. Its legal-name, domain, parent, and entity-type fields remain identity/search hints, never accepted profile facts. The normal initial workflow resolves first, then performs duplicate matching, creates or reuses a Company, and begins discovery. Accepted identity is trusted for targeted enrichment. Legacy candidate-driven grounding remains only for compatibility with existing stored runs and is intentionally isolated from the Day-6 initial path.

`ISourceSemanticReranker` provides bounded, user-facing same-entity/related/different-entity relevance reasons. It can alter default source selection but never removes human review or replaces deterministic source classification.

`ResearchSettingsEntity` persists safe model roles, grounding/reranking preferences, and provider priorities. It never stores secrets. Refresh creates another ResearchRun and profile confirmation remains the only path to a new immutable version. `ProfileDiffService` persists deterministic `ProfileChange` records between accepted versions.

`CompanyMonitoringSetting` is serviced by one ASP.NET Core `BackgroundService`. It runs only while the API is running, creates a review-ready candidate, and never auto-confirms a Company Profile.

## Research and sources

`ResearchRun` owns the staged lifecycle and truthful counters. `ResearchCandidate` preserves discovery, classification, recommendation, selection, and acquisition state. The server accepts persisted candidate IDs for acquisition rather than arbitrary client URLs.

Supported source kinds include OfficialWebsite, OfficialDocument, BusinessRegistry, TopCv, LinkedIn, News, ExternalWebsite, and SearchResult. Source classification, URL normalization, deduplication, recommendation reasons, field-aware authority policy, and bounded same-domain official-site planning are application concerns. TopCV parsing supplements rather than replaces the original `SourceDocument`.

`SourceDocument` is the durable raw evidence record. Duplicate content increments duplicate counters instead of pretending another document was persisted. `ResearchEvent` stores safe operational metadata such as stage, provider, duration, status, and sanitized errors—not keys, headers, cookies, hidden reasoning, or unbounded document content.

Day 5 evaluates qualitative `ResearchTarget` coverage (`Missing`, `Weak`, `Supported`, `Strong`) before profile generation and after acceptance. Recommended roots are selected deterministically for complementary same-entity evidence; five is a maximum, not a document limit. An approved official root may yield bounded same-domain pages. A first-profile strengthening run combines already approved company evidence with newly acquired gap evidence; it never discards the dossier the user reviewed. Targeted enrichment reuses `ResearchRun`, then creates a server-owned patch candidate that may only alter target-authorized field groups; confirmation still appends a complete immutable profile version. `BusinessDirectory` (including MaSoThue) is distinct from `OfficialBusinessRegistry`; registered activities are not marketed products.

## Profiles and provenance

Company identity remains separate from research output. Gemini receives a bounded package of identity hints and acquired source evidence, returns structured JSON, and must leave unsupported values unknown. `ProfileInputBuilder` and `CompanyProfileValidator` validate source IDs, company ownership, permitted field paths, and duplicate evidence references.

Generated candidates are not accepted facts. A human confirmation creates an immutable `CompanyProfileVersion` with `ProfileEvidence`. The latest server-owned candidate can be retrieved by research run for review; the latest accepted version is available through the Company workspace, and previous versions remain in storage.

## Runtime model preferences

Gemini configuration establishes startup defaults. The local Settings page may switch the approved Fast model choice between `gemini-3.5-flash-lite` and `gemini-3.8-flash` through a runtime-only preference service. The choice applies to new profile-generation scopes; preferences reset on API restart and do not alter or reveal secrets.

## Workspace composition

The React application uses a fixed, collapsible desktop sidebar and a mobile drawer, contextual top bar, System Status route, reusable source cards/icons, and Settings. Company workspace tabs are Overview, Sources, Investigations, Changes, and Monitoring. A desktop Ask RAVEN dock reflows the dossier rather than overlaying it; on mobile it becomes a drawer. The frontend may render company context and composer modes, but Hung owns the Ask RAVEN backend/conversation contract. Source icons use safe domain favicon resolution with provider-aware and Phosphor fallbacks.

`CompanyLifecycleService` is the mutation boundary for archive, restore, permanent deletion, and user-confirmed merge. Merge is transactional and retains compatible research, source, profile, provenance, monitoring, and saved-investigation relationships. It rewrites moved serialized profile/candidate identity references and reorders the combined immutable profile history by confirmation time, so the current accepted profile remains a valid, hydrated snapshot rather than being masked by a sparse retained Company row. Missing stable Company identity values are filled from the merged record; existing canonical values remain authoritative. `CompanyWorkspaceReviewService` is read-only: it evaluates deterministic health and duplicate groups, and its optional AI seam can only return recommendations.

## Future seams

The existing evidence model leaves deliberate attachment points for:

```text
SourceDocument → SourceChunk → embeddings → company-filtered retrieval
→ Ask RAVEN answers with citations
```

`SavedResearchArtifact` preserves historical investigation material for a company. It validates source ownership and does not mutate an accepted profile. It belongs in Investigations; it can only begin a target-scoped profile-improvement flow, not update an accepted profile directly. Ask RAVEN is persistent profile-grounded Chat: factual company answers cite accepted-profile evidence, while safe greetings and navigation guidance need not invent citations. Chat web lookup is not implemented.

RAG, MCP, Crawl4AI Cloud, and web-enabled Ask RAVEN turns remain future work. They must preserve the same source provenance and deterministic Fast Research path.
