# RAVEN Architecture
## Current implementation boundary

The repository currently contains the Day 1 foundation: an ASP.NET Core API with migrated SQLite persistence, Company create/list/get endpoints, development CORS, and a timeout-bounded Crawl4AI Local availability probe. It also contains the React/Vite company-flow UI and Docker Compose for Crawl4AI Local. Research workflows and provider adapters remain intended architecture rather than implemented functionality. See [STATUS.md](STATUS.md) for the live inventory.

## Technology stack

~~~text
Frontend        React + TypeScript
Backend         ASP.NET Core
Database        SQLite
ORM             Entity Framework Core

Search          Brave / Exa / Crawl4AI Cloud / Firecrawl
Crawler         Crawl4AI Local / Crawl4AI Cloud / Firecrawl

AI              Gemini
Agents          Microsoft Agent Framework
MCP             Exa MCP / optionally Firecrawl MCP

RAG             embeddings + vector retrieval
Infrastructure  Docker
~~~

## Provider architecture

Search, crawling, and AI are independent capabilities. Provider-specific work stays behind capability interfaces; workflows operate on interfaces and record requested and actual providers, including fallback.

~~~text
ISearchProvider
├── Brave
├── Exa
├── Crawl4AI Cloud
└── Firecrawl

ICrawlerProvider
├── Crawl4AI Local
├── Crawl4AI Cloud
└── Firecrawl

IAiModelProvider
├── Gemini
└── OpenAI-compatible
~~~

The balanced preset orders search as Brave, Exa, Crawl4AI Cloud, Firecrawl; and crawling as Crawl4AI Local, Crawl4AI Cloud, Firecrawl. Easy Cloud uses Crawl4AI Cloud for both. Cloud Robust uses Exa and Firecrawl. Fall back only for retryable failures such as timeouts, rate limits, temporary network faults, and 5xx responses; invalid configuration, auth failure, bad input, and unsupported capability fail visibly.

## Fast Research

Fast Research is deterministic: application code controls ordinary profile generation and an LLM does not autonomously decide the workflow.

~~~text
Company → Search provider → URL ranking → Crawler provider → Source documents
→ Cleaning/chunking → Embeddings → RAG → Gemini Flash-Lite
→ Structured profile → Validation → Persistence
~~~

The workflow creates an observable ResearchRun, persists clean source content, and derives a validated profile from selected evidence. Missing scalar data is null; missing collections are empty. Model IDs remain configuration-driven.

## Deep Research

Deep Research is agentic and reserved for investigative questions.

~~~text
Question → CompanyResearchAgent → Internal RAG → Enough evidence?
Yes → grounded answer with citations
No  → search / crawl / MCP → Gemini Flash → grounded answer
~~~

Microsoft Agent Framework hosts CompanyResearchAgent. It follows RAG-first behavior and may inspect profiles/sources or use search, crawling, Exa MCP, and optionally Firecrawl MCP. Application code owns persistence. The agent must not receive unrestricted SQL or database-mutation tools. Answers distinguish stored evidence from newly gathered evidence.

## Core data model

~~~text
Company
 ├── ResearchRuns
 ├── SourceDocuments
 │     └── SourceChunks
 ├── CompanyProfileVersions
 │     └── ProfileEvidence
 └── ProfileChanges
~~~

ResearchRun records lifecycle, requested/actual providers, model, counts, and errors. SourceDocument retains normalized source content; SourceChunk supports retrieval. CompanyProfileVersion is immutable. ProfileEvidence maps profile fields to source/chunk evidence. ProfileChange records old/new values. EF models and migrations are the detailed schema authority.

## Tracking

~~~text
Research history + Profile version history + Change detection
~~~

A successful refresh creates a new profile version rather than overwriting history. Scheduled monitoring is optional after manual refresh works.

## Important architecture decisions

- SQLite is the portable MVP source of record.
- Search and crawling are separate provider abstractions.
- Brave plus Crawl4AI Local is the recommended default.
- Profiles are versioned rather than overwritten.
- Fast Research is deterministic; Agent Framework is primarily for Deep Research.
- Internal RAG is consulted before external Deep Research where appropriate.
- MCP extends agent capabilities; it does not replace ordinary provider APIs.
- Crawl4AI availability is checked on demand; its absence must not prevent API startup.
