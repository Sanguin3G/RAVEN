# RAVEN Implementation Plan

This plan guides a two-person project. [STATUS.md](STATUS.md) is the integrated implementation record.

## Roles

**Person A — Backend / AI primary** owns API, persistence, provider adapters, research orchestration, and retrieval. **Person B — Frontend / Product primary** owns the React workspace, source presentation, profile experience, and future question-answering surfaces.

Both collaborators share contracts, architecture decisions, integration, testing, and release quality. Coordinate before changing shared DTOs, Program.cs, frontend bootstrap/routing, Docker Compose, migrations, or STATUS.md.

## M1 — Core Research

**Status: implemented vertical slice; stabilize and demo.**

```text
Company → Brave → source review → Crawl4AI Local → evidence
→ Gemini → profile confirmation → React dossier
```

Delivered:

- rich identity input and duplicate suggestions;
- staged discovery, selection, acquisition, evidence review, and profile review;
- source taxonomy, bounded official-domain planning, TopCV parsing, LinkedIn and registry discovery;
- evidence provenance, profile candidates, validation, immutable profile versions, and research events;
- Company workspace, source UI, responsive shell, provider status, and local runtime model choices.

Remaining M1 work is quality work: real-provider failure handling, usability polish, demo data, regression coverage, and documentation accuracy.

## M2 — Research Intelligence and Company Knowledge

**Day-4 implementation: integrated on `main`.**

Implemented: profile refresh, change detection, AI target grounding, semantic source relevance, persisted research/provider settings, Exa retrieval, in-process monitoring, bounded Deep Research, and saved research artifacts. Firecrawl remains legacy compatibility code only and is not active routing.

Remaining M2 work: source chunking, embeddings, company-filtered retrieval, and web-enabled Ask RAVEN turns. Persistent profile-grounded Chat with citations is integrated. Day 8 managed-research attachments are explicit conversation state; their eventual prompt grounding belongs to Hung's Ask RAVEN context-extension contract, not a parallel Huy-side Chat implementation.

**Exit condition:** accepted knowledge survives restart, can be reopened and questioned with citations, and a refresh preserves historical evidence and profiles.

## M3 — Investigative Research

**Target: after M2.**

Extend the bounded Deep Research backend with RAG-first retrieval, cancellation, richer diagnostics, controlled external research through normal provider interfaces, and optional MCP. Add Crawl4AI Cloud only when its value is demonstrated by the workflow.

**Exit condition:** RAVEN can explain whether an answer came from stored evidence or newly acquired evidence, while keeping persistence application-owned.

## Day 8 — Managed Research and Investigation interoperability

**Status: PASS; integrated into `main`.**

Delivered: asynchronous Exa Agent jobs with durable polling/recovery state; normalized claims, cited source leads, and uncertainties under Investigations; completion notification polling; and a Deep Research composer mode that leaves Chat usable. External Research Import generates a focused copyable brief for any assistant and saves pasted Markdown as reviewable, untrusted research material. Firecrawl is no longer an active route, settings choice, or provider-status surface; old persisted priorities normalize safely. Company Overview has an optional Google Maps Embed adapter and a safe external-map fallback.

Investigation attachments are explicit, durable, and removable per conversation. Hung's Ask RAVEN backend owns eventual LLM grounding of those attachments; no parallel Chat agent or prompt path was introduced.

## Research workspace and external research assist

**Status: PASS; integrated into `main`.**

The Company Investigations tab is research-first: one workspace presents Investigation objectives, origin/provenance, organized themes, claims, uncertainties, source leads, follow-up gaps, and expandable raw material. Organization is a versioned derived revision; raw research and earlier revisions remain intact. External Research Assist is a focused, provider-neutral copy/paste workflow with durable asynchronous analysis, target-specific briefs for all selected improvement areas, draft preservation, minimize/reopen behavior, explicit unverified labeling, review, save-to-Investigation, and selected-source handoff to normal RAVEN acquisition. Native, Deep, and External activities share one global research status surface with running/ready semantics. Targeted profile improvement offers distinct RAVEN Research, Deep Research, and External paths; Deep ProfileImprovement results can run before Profile v1 but remain locked and out of Workspace Review until a supported profile exists. Profile confirmation is always an explicit user action.

Workspace Review is a durable, compact queue. Terminal research is grouped by company, method, and topic so repeated failed/saved outcomes produce one actionable item. Users can acknowledge individual groups, mark research results done in bulk, or run a confirmation-protected smart cleanup for repeated issue groups. These actions clear review state only; they never delete research history or evidence.

## Research recovery and profile safety

Known research runs are restored step-by-step. Missing initial state is discarded as stale session data; a failed related restore request preserves the run and reports the specific step with retry. A failed or in-progress run cannot be confirmed, and an identity-only/name-only accepted row cannot be used as a Profile Improvement baseline. Supported-target gaps and model provenance are the product signals used to distinguish an interrupted artifact from a sparse or partial profile.

## Day 5 — Evidence coverage and workspace quality

**Status: PASS; integrated into `main`.**

Delivered scope is coverage-aware source-root selection, generic corporate-family discovery with review-only deterministic fallback, bounded target-aware official-site expansion, MaSoThue `BusinessDirectory` parsing, target-scoped profile patching, and company archive/delete/merge/workspace-review services. The final product slice adds cancellable in-process background discovery, server-owned profile-candidate retrieval, and the corresponding profile-enrichment, lifecycle, Investigations, Monitoring, and Ask RAVEN dock frontend workflows. Ask RAVEN backend ownership remains with Hung; the implementation consumes no invented conversation contract.

## Day 6 — Pre-search identity resolution

**Status: PASS**; integrated into `main` from `feat/day6-identity-resolution`.

Delivered scope is a Company-independent identity preflight endpoint, explicit website/registration fast paths, one bounded Gemini topology call for weak inputs, deterministic derivation of `Resolved`, `Ambiguous`, `NeedsMoreInfo`, or `Unknown`, distinct family/name-collision handling, parent-first identity choices, and a deliberate exact-name override for obscure companies. The frontend keeps the original research form as the single edit surface and presents guided clarification in a compact, dismissible modal. Identity options and model-provided domains/legal names remain navigation hints, not accepted profile evidence. Duplicate matching runs only after resolution, and a bounded resolved-identity snapshot is carried into each new ResearchRun.

Stabilization closes the Day-6 browser/session and workspace seams: only resumable research runs restore after reload; cancel returns to a blank research form; merged profile history remains hydrated and ordered; and Company List actions address Monitoring and targeted enrichment directly, with honest guidance when no accepted profile exists.

## Day 7 — Execution efficiency and Chat foundation

**Status: PASS; integrated into `main`.**

Delivered: bounded, sanitized execution telemetry with background batch persistence, isolated EF scopes, terminal flushes, shutdown drain, and canonical external-call rows; durable Deep Research activity with database-free monotonic sequence allocation; removal of active legacy family-search/grounding orchestration from new initial research; and an Ask RAVEN conversation-first refinement. Ask RAVEN supports persisted profile-grounded conversations, citations, bounded excerpts, citation-free greeting/capability guidance, a real Investigations handoff, and a truthful disabled Web Search slot.

Deferred deliberately: semantic-reranking changes and actual web-enabled Chat turns. Day 8 may add turn-scoped web lookup and richer Chat actions without turning Chat into Deep Research.

## Stabilization principles

Prefer small feature branches, focused reviews, mockable provider tests, and working vertical slices. Do not add distributed infrastructure merely to simulate activity; recorded run state and staged HTTP operations are sufficient for the MVP.
