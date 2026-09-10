# RAVEN Architecture

## Current implementation boundary

RAVEN is a modular ASP.NET Core API with a React/Vite client and SQLite as the system of record. The implemented M1 workflow is deterministic and staged; it does not contain RAG, agents, MCP, queues, or background workers.

```text
Company identity
→ deterministic discovery
→ persisted ResearchCandidate review
→ selected-source acquisition
→ SourceDocument evidence
→ Gemini structured profile candidate
→ deterministic evidence validation
→ human confirmation
→ immutable CompanyProfileVersion + ProfileEvidence
```

Each network step remains an ordinary HTTP operation. `ResearchRun.Stage`, status, counters, and ResearchEvent records provide truthful UI activity without invented percentage progress.

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
ICrawlerProvider      → Crawl4AiLocalProvider
IAiModelProvider      → GeminiProvider
```

Application workflows depend on those neutral capabilities rather than provider-specific DTOs. External calls are mockable in tests. Configuration and status endpoints expose only configured/available/model state; they never return credentials.

Brave discovers candidate URLs. Crawl4AI reads selected pages. SQLite preserves evidence. Gemini normalizes bounded evidence into a profile candidate. The application validates and persists accepted facts.

## Research and sources

`ResearchRun` owns the staged lifecycle and truthful counters. `ResearchCandidate` preserves discovery, classification, recommendation, selection, and acquisition state. The server accepts persisted candidate IDs for acquisition rather than arbitrary client URLs.

Supported source kinds include OfficialWebsite, OfficialDocument, BusinessRegistry, TopCv, LinkedIn, News, ExternalWebsite, and SearchResult. Source classification, URL normalization, deduplication, recommendation reasons, field-aware authority policy, and bounded same-domain official-site planning are application concerns. TopCV parsing supplements rather than replaces the original `SourceDocument`.

`SourceDocument` is the durable raw evidence record. Duplicate content increments duplicate counters instead of pretending another document was persisted. `ResearchEvent` stores safe operational metadata such as stage, provider, duration, status, and sanitized errors—not keys, headers, cookies, hidden reasoning, or unbounded document content.

## Profiles and provenance

Company identity remains separate from research output. Gemini receives a bounded package of identity hints and acquired source evidence, returns structured JSON, and must leave unsupported values unknown. `ProfileInputBuilder` and `CompanyProfileValidator` validate source IDs, company ownership, permitted field paths, and duplicate evidence references.

Generated candidates are not accepted facts. A human confirmation creates an immutable `CompanyProfileVersion` with `ProfileEvidence`. The latest accepted version is available through the Company workspace; previous versions remain in storage.

## Runtime model preferences

Gemini configuration establishes startup defaults. The local Settings page may switch the approved Fast and Deep model choices between `gemini-3.5-flash-lite` and `gemini-3.8-flash` through a runtime-only preference service. Fast choice applies to new profile-generation scopes; preferences reset on API restart and do not alter or reveal secrets.

## Frontend composition

The React application uses a fixed, collapsible desktop sidebar and a mobile drawer, contextual top bar, System Status route, Company workspace tabs, reusable source cards/icons, Settings, and an explicit Ask RAVEN handoff surface. Source icons use safe domain favicon resolution with provider-aware and Phosphor fallbacks.

## Future seams

The existing evidence model leaves deliberate attachment points for:

```text
SourceDocument → SourceChunk → embeddings → company-filtered retrieval
→ Ask RAVEN answers with citations
```

Deep Research, Microsoft Agent Framework, MCP, Exa, Firecrawl, scheduled monitoring, and change detection remain future work. They must preserve the same source provenance and deterministic Fast Research path.
