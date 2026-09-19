# RAVEN Architecture

## Current implementation boundary

RAVEN is a modular ASP.NET Core API with a React/Vite client and SQLite as the system of record. Fast Research remains deterministic and staged. Bounded Deep Research and small in-process workers are implemented, but RAG, MCP, distributed queues, and microservices are not.

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

Each network step remains an ordinary HTTP operation. `ResearchRun.Stage`, status, counters, and ResearchEvent records provide truthful UI activity without invented percentage progress. Developer execution telemetry is a separate, bounded diagnostic stream: sanitized Search/Crawl/AI rows enqueue without blocking the caller and a background service persists batches using a fresh EF scope. A terminal run boundary requests a best-effort flush so execution summaries are promptly available. Business state is never dropped; Deep Research activity remains durable user-visible state and is not put in the lossy telemetry queue. An in-process channel-backed worker may run discovery after a `202 Accepted` start request; it supports cancellation and active-run visibility, but is intentionally non-durable, so queued work does not survive an API restart. Client session restoration is intentionally narrower: only known active/user-actionable stages are restored. A missing initial run is stale session state and resets to a blank form; a failure while rehydrating a known run preserves the session and reports the specific restore step with retry instead of converting the run into `RESEARCH FAILED`.

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
                      → ExaSearchProvider
ICrawlerProvider      → Crawl4AiLocalProvider / ExaCrawlerProvider
IManagedResearchAgent → ExaAgentClient
IAiModelProvider      → GeminiProvider
```

Application workflows depend on those neutral capabilities rather than provider-specific DTOs. External calls are mockable in tests. Configuration and status endpoints expose only configured/available/model state; they never return credentials.

Provider routing uses persisted priorities and falls back only after retryable rate-limit, timeout, unavailable, or retrieval failures. Authentication, configuration, invalid requests, and malformed responses remain visible failures. Brave/Exa discover candidate URLs; Crawl4AI Local and Exa Contents read selected pages. SQLite preserves evidence. Gemini normalizes bounded evidence into a profile candidate. The application validates and persists accepted facts. Firecrawl adapters remain only as legacy compatibility code; it is not routed, configured in active status, or selectable in settings.

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

Profile creation is strictly confirmation-gated. `CompanyProfileWorkflowService` accepts confirmation only when the run is `Completed` and `AwaitingProfileConfirmation`; failed, cancelled, or still-in-progress runs cannot create a profile. The generated candidate must have model provenance, at least one supported research target, and source-backed evidence. `CompanyProfileReadiness` classifies identity-only, sparse, partial, and complete snapshots. A persisted name-only row from an interrupted workflow remains historical data but cannot unlock Deep review or targeted Profile Improvement.

## Runtime model preferences

Gemini configuration establishes startup defaults. The local Settings page may switch the approved Fast and Deep model choices between `gemini-3.5-flash-lite` and `gemini-3.8-flash` through a runtime-only preference service. Fast choice applies to new profile-generation scopes; preferences reset on API restart and do not alter or reveal secrets.

## Workspace composition

The React application uses a fixed, collapsible desktop sidebar and a mobile drawer, contextual top bar, System Status route, reusable source cards/icons, and Settings. Company workspace tabs are Overview, Sources, Investigations, Changes, and Monitoring. A desktop Ask RAVEN dock reflows the dossier rather than overlaying it; on mobile it becomes a drawer. The frontend may render company context and composer modes, but Hung owns the Ask RAVEN backend/conversation contract. Source icons use safe domain favicon resolution with provider-aware and Phosphor fallbacks.

`CompanyLifecycleService` is the mutation boundary for archive, restore, permanent deletion, and user-confirmed merge. Merge is transactional and retains compatible research, source, profile, provenance, monitoring, Deep Research, and saved-investigation relationships. It rewrites moved serialized profile/candidate identity references and reorders the combined immutable profile history by confirmation time, so the current accepted profile remains a valid, hydrated snapshot rather than being masked by a sparse retained Company row. Missing stable Company identity values are filled from the merged record; existing canonical values remain authoritative. `CompanyWorkspaceReviewService` is read-only: it evaluates deterministic health and duplicate groups, and its optional AI seam can only return recommendations. A separate `WorkspaceResearchReviewState` records only user acknowledgement watermarks for the compact research queue; its acknowledge and smart-cleanup endpoints preserve all runs, investigations, sources, and evidence.

## Future seams

The existing evidence model leaves deliberate attachment points for:

```text
SourceDocument → SourceChunk → embeddings → company-filtered retrieval
→ Ask RAVEN answers with citations
```

`DeepResearchRun` is a bounded, company-scoped backend operation implemented through Microsoft Agent Framework and `IChatClient`. Its model may invoke only read-only profile/source lookup, configured provider-routed search/crawl, and stored-source text search. Tool/search/crawl/document/duration budgets prevent open-ended work. `DeepResearchActivityRecord` persists only safe activity labels and source IDs—not prompts, credentials, raw tool outputs, or hidden reasoning.

`SavedResearchArtifact` preserves a completed investigation only when requested. It validates source ownership and does not mutate an accepted profile. It belongs in Investigations; it can only begin a target-scoped profile-improvement flow, not update an accepted profile directly. External Research Import produces the same untrusted research material from a generated brief and pasted Markdown; provider URLs and claims remain review context and are not re-searched or re-crawled by External Assist.

Managed jobs carry an explicit purpose. `General` investigations remain reviewable research material without an accepted profile; `ProfileImprovement` investigations may complete in the background before Profile v1 exists, but remain visibly locked and excluded from Workspace Review until an accepted profile exists. After Profile v1 is accepted, the same preserved result can enter the existing server-owned targeted profile-patch confirmation flow and create a new immutable profile version. Neither Deep nor External research silently mutates the native candidate or accepted profile.

Managed AI Research is a durable asynchronous workflow: an Exa Agent run is queued, polled by an isolated background worker, normalized into a company Investigation, and surfaced through lightweight client polling/notification. It never mutates the accepted profile. Ask RAVEN can persist explicit, removable Investigation attachments per conversation. Actual LLM grounding from those attachments is intentionally deferred to Hung's supported Ask RAVEN context-extension seam; this branch does not introduce a second Chat agent or prompt path. Chat web lookup remains Hung-owned.

An Investigation is the durable research workspace, not a provider-result type. Native Research, Managed AI/Deep Research, and External AI-assisted research share the same review surface and retain their origin metadata. Raw material is preserved; `InvestigationOrganizationRevision` is a replaceable, versioned derived view and cannot mutate accepted profile truth. External analysis is a durable queued task: deterministic Markdown parsing produces reviewable claims, source leads, and uncertainties, while the original response remains available when analysis fails. Deep/External results can enter the existing server-owned targeted profile-patch workflow as imported material; the user still reviews and confirms an immutable profile version, and no second crawl is launched for provider citations.

The client has one global research activity surface. Native runs retain their existing durable status, while Deep and External activities have typed running/ready/failed semantics and reopen destinations. Modal minimization only changes the view; the durable task continues. Profile candidates remain stable snapshots while any related activity runs, so later findings create a new review opportunity rather than silently changing what the user confirmed. Managed AI Research settings expose provider-neutral depth (`Adaptive`, `Focused`, `Standard`, `Thorough`, `Exhaustive`) and map those values inside the provider boundary.

Workspace Review groups terminal research by company, method, and normalized topic. The latest occurrence is shown once, repeated failures are summarized, and an acknowledgement hides the group only through the recorded result timestamp; a newer result reappears automatically. Completed ProfileImprovement Deep results without a usable accepted profile are preserved in Investigations but excluded from the review queue.

RAG, MCP, Crawl4AI Cloud, and web-enabled Ask RAVEN turns remain future work. They must preserve the same source provenance and deterministic Fast Research path.
