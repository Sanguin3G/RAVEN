# RAVEN Agent Instructions
RAVEN is an AI-assisted Company Intelligence Platform.

Before substantial work, read:

1. `docs/PROJECT.md`
2. `docs/ARCHITECTURE.md`
3. `docs/STATUS.md`
4. `docs/PLAN.md`
5. the GitHub Issue or task assigned to you

`PROJECT.md` describes intended product scope. `ARCHITECTURE.md` defines technical boundaries. `PLAN.md` gives milestone direction. `STATUS.md` records what is integrated. When they disagree, inspect the code and treat code and `STATUS.md` as authoritative.

## Product priority

~~~text
Company → public-source research → standardized Company Profile
→ evidence storage → centralized knowledge → refresh/history/change tracking
~~~

RAG, Agent Framework, MCP, and provider diversity enhance this workflow; they must not prevent the core product from working.

## Architecture rules

- Search, crawling, and AI inference are separate provider capabilities. Do not couple domain logic directly to Brave, Exa, Crawl4AI, Firecrawl, or Gemini APIs.
- Fast Research is deterministic. Deep Research may use Microsoft Agent Framework and MCP, and should use internal RAG before unnecessary external research.
- Profiles are versioned; refresh preserves research and profile history.
- Unknown company information remains unknown. Do not fabricate schema values.
- Generated factual claims should retain evidence/source relationships where practical.
- LLMs must not receive unrestricted database mutation or raw SQL tools.
- External provider calls must be mockable; normal tests must not require real credentials.

## Day-5 evidence and workspace guardrails

- Five recommended research roots are an upper bound, not a five-document limit. Do not pad recommendations with unrelated pages.
- Coverage drives bounded follow-up research. When the budget is exhausted, unsupported facts remain unknown.
- A targeted profile patch may change only its authorized research targets and must preserve every unrelated accepted field exactly.
- `BusinessDirectory` (including MaSoThue) is not `OfficialBusinessRegistry`; registered business activities are not marketed products or services.
- AI may recommend workspace cleanup, aliases, or duplicates, but it must never archive, delete, or merge Companies without an explicit user confirmation.
- A Saved Investigation is not accepted Company Profile truth. Ask RAVEN, Deep Research, Investigations, and Monitoring are distinct product surfaces.
- Hung owns the Ask RAVEN backend, conversation persistence, Quick Ask orchestration, and prompt/context contracts. Frontend work must consume that contract or remain behind an adapter boundary; never fabricate chat answers.

## Performance and orchestration

- Spend complexity only when uncertainty justifies it. Avoid repeated Search → LLM → Search → LLM loops by default, and keep external I/O bounded.
- Independent external requests may use bounded concurrency when safe. Persist meaningful workflow state rather than every heartbeat.
- Observability must not materially dominate workflow latency. Settings that define one ResearchRun should normally be snapshotted for that run.

## Observability

- User-visible Research Activity is distinct from developer Execution Telemetry. Activity is product UX; telemetry is bounded operational detail.
- One logical external operation should normally map to one execution record/lifecycle. Category, Operation, and Status are separate concepts.
- Provider, AI, and tool instrumentation belongs at focused boundaries rather than being manually scattered through coordinators.
- Never log provider secrets, authorization headers, hidden chain-of-thought, full prompts, or giant raw provider responses. SourceDocument is evidence storage, not a telemetry substitute.
- Run summaries aggregate calls, latency, token usage, fallbacks, and failures. Telemetry failure must not make normal research fail.

## Provider presets

- RAVEN Resilient means ordered provider fallback, not load balancing or parallel racing. The preferred provider runs first; compatible fallbacks run only after eligible failure.
- Domain, backend, frontend, and documentation terminology must use Resilient.

## Maintainability

- Do not grow an orchestration or page file merely because it is convenient. Around 500–700 lines, review whether a production source has multiple responsibilities; files above roughly 800 lines require justification or a decomposition plan.
- Generated EF migrations, designers, and snapshots are exempt. Split by cohesive responsibility, not arbitrary line count, and do not create a swarm of tiny Manager/Helper/Processor classes.
- React pages coordinate page-level behavior; reusable workflow stages belong in components or hooks. Backend coordinators orchestrate; discovery, acquisition, telemetry, parsing, ranking, and persistence policy belong in focused boundaries.

## Identity — Day-6 direction

These are future architecture principles; Day 5.5 does not implement them.

- Resolve who the user means before expensive public-source research. Explicit identifiers (official domain, tax/registration ID) outweigh probabilistic inference.
- Model prior knowledge may assist identity resolution but is not accepted Company Profile evidence. Public sources support profile facts; RAVEN may return Resolved, Ambiguous, NeedsMoreInfo, or Unknown.
- When uncertain, request the minimum useful clarification: country, website, legal name, tax/registration ID, or useful headquarters/region. Distinguish corporate-family ambiguity from unrelated similar names; present a confidently known parent above subsidiaries.
- Trust an accepted identity during targeted enrichment unless identity itself is under review. Unsupported identity claims must never silently become accepted profile truth.

## Team ownership

- Hung owns Ask RAVEN backend. Huy-side workspace code may integrate against Hung's published contract but must not duplicate that backend.

## Scope discipline

This is a two-person, approximately 20-working-day project. Prefer working, testable vertical slices. Do not add major infrastructure or abstractions without a current feature need, including microservices, Kafka, Redis, Kubernetes, multi-agent swarms, or speculative enterprise patterns.

## Parallel-agent rules

Stay inside the assigned scope. Avoid modifying shared integration hotspots unless explicitly assigned: `Program.cs`, root frontend routing/bootstrap, `docker-compose.yml`, shared DTOs/contracts, EF migration ordering, `docs/STATUS.md`, and `AGENTS.md`.

Prefer self-contained modules and narrow registration helpers so the integration owner can complete final wiring. Do not silently redesign shared contracts; report architectural conflicts.

## Documentation

Do not update every document for every code change. Update `PROJECT.md` for material scope changes, `ARCHITECTURE.md` for material architecture changes, `PLAN.md` for milestone or ownership changes, and `DEVELOPMENT.md` for setup or workflow changes. Only the integration/orchestration owner normally updates `STATUS.md`. Avoid new Markdown files unless established documents genuinely cannot hold the information.

## Secrets and completion

Never commit or log API keys, tokens, passwords, credentials, or populated `.env` files. Use environment variables, user secrets, or the configured secret mechanism.

Run relevant tests before reporting completion and state only tests actually run. A completion report must include a summary, files changed, tests and results, integration requirements, and unresolved assumptions. Code on an isolated branch is not fully integrated merely because it exists.
