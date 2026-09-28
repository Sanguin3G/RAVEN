# RAVEN Agent Instructions

RAVEN turns public-source research into reviewable, evidence-backed Company Profiles. Before substantial work, read `docs/ARCHITECTURE.md`, `docs/STATUS.md`, and the assigned task. Inspect code when documentation disagrees.

## Product boundaries

- Resolve company identity before expensive public-source research. Explicit identifiers outweigh probabilistic inference; model prior knowledge is a navigation hint, not profile evidence.
- Keep Search, Crawl, and AI inference behind separate, mockable provider capabilities. Normal tests must not need provider credentials or paid traffic.
- Research is bounded. Unsupported facts remain unknown. Five recommended roots are a maximum, not a document limit.
- Only explicit user confirmation creates an accepted, immutable Company Profile version. Targeted patches may change only authorized fields and must preserve unrelated accepted values.
- Keep source relationships for generated factual claims where practical. A saved Investigation, Briefing, or Chat Web snapshot is research context, not accepted Company Profile truth.
- `BusinessDirectory`, including MaSoThue, is distinct from `OfficialBusinessRegistry`. Registered activities are not marketed products or services.
- Do not give an LLM unrestricted database mutation or raw SQL tools. Company archive, deletion, and merge require explicit user confirmation.

## Implementation

- Prefer focused vertical slices and current service seams. Add an abstraction only when current code has more than one real responsibility for it. Avoid speculative infrastructure.
- Split orchestration and page files by cohesive responsibility before they become difficult to review. Generated EF migrations and snapshots are exempt from source-size guidance.
- Persist durable business state in SQLite. Browser storage may hold convenience state such as active conversation IDs, drafts, and device preferences, but not full Chat history or provider secrets.
- User-visible Research Activity is durable product state. Execution telemetry is bounded, sanitized, buffered, and best effort; telemetry failure must not fail research.
- RAVEN Resilient means ordered fallback after eligible failure, never provider racing.
- Keep secrets server-side and out of commits, logs, telemetry payloads, and CI.

## Working practice

- Preserve existing work and inspect migration history before changing persistent models. Do not rewrite historical migrations.
- Test the behavior affected by a change, then run the relevant full checks before handoff. External provider calls in automated tests use fakes or fixtures.
- Use subagents only for bounded independent work that gives real parallel value. The primary agent owns integration and verification.
- Update `docs/ARCHITECTURE.md` for material architecture changes and `docs/DEVELOPMENT.md` for setup or workflow changes. Keep `docs/STATUS.md` as a truthful validation baseline.
- Report changed files, checks actually run, integration needs, and unresolved limitations. Branch code is not integrated into `main` until it is merged.
