# RAVEN Project

## Business problem

Organizations research prospective companies across scattered public sources, then reconstruct the same facts whenever they need an updated view. RAVEN turns this work into reusable Company Profiles with inspectable evidence.

## Core user flow

```text
Identity preflight and research hints
→ resolve or clarify the intended organization
→ find likely duplicate or create separately
→ discover public sources
→ researcher selects sources
→ acquire and preserve evidence
→ generate a structured profile candidate
→ validate provenance
→ human confirmation
→ immutable Company Profile version
```

User-entered identity data is a research hint, not automatically a verified profile fact. Unknown information remains unknown: unsupported scalar values are `null`, and unsupported collections are empty.

## Current product surface

RAVEN currently supports:

- rich Company identity fields and non-blocking duplicate suggestions;
- deterministic Brave discovery with bounded official-domain planning;
- persisted candidate review, source taxonomy, recommendation reasons, and source selection;
- Crawl4AI Local evidence acquisition with truthful success, failure, duplicate, and document counters;
- TopCV, LinkedIn, business-registry, official-document, news, and external-source classification;
- structured Gemini profile candidates, deterministic evidence-reference validation, and explicit confirmation;
- immutable Company Profile versions, ProfileEvidence, SourceDocuments, and research events;
- a React Company workspace with Overview, Sources, Investigations, Changes, and Monitoring; Ask RAVEN is a company-scoped dock, not a dossier tab.
- topology-only Gemini identity assistance with deterministic workflow policy, explicit-identifier fast paths, clarification, and source relevance with deterministic fallback.
- persistent research settings, manual refresh, profile history, deterministic changes, and review-only monitoring.
- native Brave/Exa Search and Crawl4AI/Exa Contents routing; managed asynchronous Exa Agent research; and saved Investigation contracts.
- provider-neutral External Research Import: RAVEN creates a copyable focused brief for any assistant and imports pasted Markdown as untrusted research material.
- explicit, removable Investigation attachments per Ask RAVEN conversation. Attached material remains separate from accepted profile truth; Hung's Chat backend owns eventual LLM grounding.
- coverage-aware company research, target-scoped evidence enrichment, protected profile patch confirmation, explicit archive/delete/merge lifecycle operations, and read-only workspace review recommendations.
- cancellable background research runs, active-run visibility, server-owned profile-candidate retrieval, parent-first corporate-family choices, and a guided clarification path for weak or unknown identities.
- persistent profile-grounded Ask RAVEN conversations with stored citations and bounded source excerpts; greetings and product guidance remain conversational while company facts remain evidence-gated.
- bounded, best-effort execution telemetry batch-persisted outside the provider-call critical path; durable Deep Research activity remains separate.

Initial profile creation is a user-controlled boundary. RAVEN may generate a candidate, but only explicit user confirmation can create an immutable Company Profile version. Failed or still-running runs cannot be confirmed, and an identity-only/name-only profile row is not a usable Profile Improvement baseline. The workspace uses supported-target coverage gaps plus model-generation provenance to distinguish a real sparse/partial profile from an interrupted workflow artifact.

The three research methods remain intentionally distinct: Native RAVEN Research is the fast integrated default; Deep Research is asynchronous and its ProfileImprovement results stay locked and outside Workspace Review until a usable Profile v1 exists; External AI Assist is provider-neutral, unverified material that also requires the normal review and confirmation path.

## Company Profile

The durable dossier answers: **what is this company?** It includes identity, classification, summary, products/services, markets, leadership, locations, public links, and field-level evidence where available. It does not invent revenue, valuation, funding, or other unsupported facts.

## Priorities

### P0 — delivered in M1

```text
Company management     Source discovery and acquisition
Evidence preservation  Standardized profiles
SQLite persistence     Profile versions
Research activity      Human review and confirmation
```

### P1 — delivered in Days 4–7

```text
Identity preflight     Source relevance
Profile refresh        Change detection
Provider routing       Monitoring
Bounded Deep Research  Saved investigations
Ask RAVEN Chat         Buffered execution telemetry
```

### P2 — later

```text
Source chunks / RAG                Web-enabled Ask RAVEN turns
MCP                                Crawl4AI Cloud
Notifications                      Advanced analytics
```

## Non-goals

RAVEN is not a distributed enterprise platform during this project. The MVP excludes microservices, Kafka, Kubernetes, unrestricted crawler recursion, and large multi-agent swarms. Technical boundaries are defined in [ARCHITECTURE.md](ARCHITECTURE.md).
