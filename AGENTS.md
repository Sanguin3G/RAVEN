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
