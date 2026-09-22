# RAVEN Agent Instructions
RAVEN is an AI-assisted Company Intelligence Platform.

Before substantial work, read `docs/ARCHITECTURE.md`, `docs/STATUS.md`, and the assigned task. When they disagree, inspect the code and treat code and `STATUS.md` as authoritative.

## Product priority

~~~text
Company → public-source research → standardized Company Profile
→ evidence storage → centralized knowledge → refresh/history/change tracking
~~~

RAG, Agent Framework, MCP, and provider diversity enhance this workflow; they must not prevent the core product from working.

## Architecture rules

- Search, crawling, and AI inference are separate provider capabilities. Do not couple domain logic directly to Brave, Exa, Crawl4AI, or Gemini APIs.
- Fast Research is deterministic. Deep Research may use Microsoft Agent Framework and MCP, and should use internal RAG before unnecessary external research.
- Profiles are versioned; refresh preserves research and profile history.
- Unknown company information remains unknown. Do not fabricate schema values.
- Generated factual claims should retain evidence/source relationships where practical.
- LLMs must not receive unrestricted database mutation or raw SQL tools.
- External provider calls must be mockable; normal tests must not require real credentials.

## Evidence and workspace guardrails

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

### Execution telemetry

- Developer execution telemetry is buffered and best effort; business/product state is never lossy telemetry.
- User-visible activity remains distinct, durable workflow state.
- One logical Search, Crawl, or AI call normally emits one canonical execution row; active code does not emit legacy Requested/Completed pairs.
- Buffered telemetry persistence uses an isolated EF scope. A telemetry failure or queue overflow must not fail product work.

## Provider presets

- RAVEN Resilient means ordered provider fallback, not load balancing or parallel racing. The preferred provider runs first; compatible fallbacks run only after eligible failure.
- Domain, backend, frontend, and documentation terminology must use Resilient.

## Maintainability

- Do not grow an orchestration or page file merely because it is convenient. Around 500–700 lines, review whether a production source has multiple responsibilities; files above roughly 800 lines require justification or a decomposition plan.
- Generated EF migrations, designers, and snapshots are exempt. Split by cohesive responsibility, not arbitrary line count, and do not create a swarm of tiny Manager/Helper/Processor classes.
- React pages coordinate page-level behavior; reusable workflow stages belong in components or hooks. Backend coordinators orchestrate; discovery, acquisition, telemetry, parsing, ranking, and persistence policy belong in focused boundaries.

## Identity architecture

- Resolve who the user means before expensive public-source research. Explicit identifiers (official domain, tax/registration ID) outweigh probabilistic inference.
- Model prior knowledge describes identity topology only; deterministic RAVEN policy derives Resolved, Ambiguous, NeedsMoreInfo, or Unknown. One logical model call is allowed for a weak identity attempt; no public Search or Crawl occurs before resolution.
- When uncertain, request the minimum useful clarification: country, website, legal name, tax/registration ID, or useful headquarters/region. Distinguish corporate-family ambiguity from unrelated similar names; present a confidently known parent above subsidiaries.
- Model prior knowledge and selected identity metadata are navigation hints, not accepted Company Profile evidence. Public sources support profile facts. Explicit identifiers bypass unnecessary AI; accepted identity is trusted during targeted enrichment unless identity itself is under review.

## Team ownership

- Hung owns Ask RAVEN backend. Huy-side workspace code may integrate against Hung's published contract but must not duplicate that backend.

## Scope discipline

This is a two-person, approximately 20-working-day project. Prefer working, testable vertical slices. Do not add major infrastructure or abstractions without a current feature need, including microservices, Kafka, Redis, Kubernetes, multi-agent swarms, or speculative enterprise patterns.

## Parallel-agent rules

Stay inside the assigned scope. Avoid modifying shared integration hotspots unless explicitly assigned: `Program.cs`, root frontend routing/bootstrap, `docker-compose.yml`, shared DTOs/contracts, EF migration ordering, `docs/STATUS.md`, and `AGENTS.md`.

Prefer self-contained modules and narrow registration helpers so the integration owner can complete final wiring. Do not silently redesign shared contracts; report architectural conflicts.

## Codex execution policy

### Default behavior

The normal root model is GPT-5.6 Luna at xhigh reasoning. Work directly by default.

Do not spawn subagents merely because they are available, because a task is large, or because deeper investigation was requested. The root agent owns task understanding, implementation, integration, normal debugging, testing, and ordinary review.

### Delegating routine work

Use a Luna High subagent only when a subtask is clearly bounded, substantially independent of the root's immediate next step, and useful for real parallel progress or context isolation.

Good targets include independent subsystem exploration, repository-wide callsite inventories, independent log or test-failure inspection, isolated straightforward tests, mechanical changes in an isolated area, and low-risk large-code summaries.

Do not delegate tiny immediate changes, work whose result blocks the root's next step, tightly coupled edits, or ordinary implementation merely to reduce the root's workload. Prefer zero subagents for ordinary tasks and at most one or two routine workers unless the work naturally decomposes further.

### Escalating difficult judgment

Use Terra Medium as a consultant, not the normal implementation agent, when architecture is materially ambiguous; tradeoffs are consequential; normal investigation leaves the root cause unclear; two serious attempts have failed; correctness depends on subtle cross-module invariants; or the root remains low-confidence despite relevant evidence.

The consultant should diagnose, compare approaches, identify invariants and risks, and recommend a direction. Luna xhigh resumes ownership for implementation and verification.

Use Terra High only for unusually difficult or high-cost correctness questions, such as subtle concurrency, security-sensitive design, destructive migrations or data-loss risk, complex transactional correctness, or a problem unresolved after Terra Medium. Do not use Terra High for routine implementation.

### Context passed to consultants

Give consultants the smallest useful handoff: problem statement, relevant files or components, observed evidence, approaches already tried, and the exact decision required. Prefer fresh or minimally forked consultant context over a huge conversation history when the runtime permits it.

### Review policy

Do not automatically spawn a reviewer after every task. Luna xhigh normally reviews its own diff and runs the appropriate tests. Consider an independent Terra Medium review only for architectural decisions, public contracts, persistent data or migrations, concurrency/security/transaction semantics, difficult or uncertain debugging, or material residual uncertainty after tests pass.

### Runtime capability fallback

Do not assume a requested subagent model or reasoning override succeeded. If the runtime does not expose or honor it, do not describe a same-model agent as Terra or Luna High; continue with Luna xhigh where possible and report that stronger-model escalation could not be performed. Correctness matters more than pretending the routing policy was followed.

## Documentation

Do not update every document for every code change. Update `ARCHITECTURE.md` for material architecture changes and `DEVELOPMENT.md` for setup or workflow changes. Only the integration/orchestration owner normally updates `STATUS.md`. Avoid new Markdown files unless established documents genuinely cannot hold the information.

## Secrets and completion

Never commit or log API keys, tokens, passwords, credentials, or populated `.env` files. Use environment variables, user secrets, or the configured secret mechanism.

Run relevant tests before reporting completion and state only tests actually run. A completion report must include a summary, files changed, tests and results, integration requirements, and unresolved assumptions. Code on an isolated branch is not fully integrated merely because it exists.
