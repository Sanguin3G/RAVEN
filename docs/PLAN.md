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

Implemented: profile refresh, change detection, AI target grounding, semantic source relevance, persisted research/provider settings, Exa/Firecrawl routing, in-process monitoring, bounded Deep Research, and saved research artifacts.

Remaining M2 work: source chunking, embeddings, company-filtered retrieval, and real Ask RAVEN answers with citations.

**Exit condition:** accepted knowledge survives restart, can be reopened and questioned with citations, and a refresh preserves historical evidence and profiles.

## M3 — Investigative Research

**Target: after M2.**

Extend the bounded Deep Research backend with RAG-first retrieval, cancellation, richer diagnostics, controlled external research through normal provider interfaces, and optional MCP. Add Crawl4AI Cloud only when its value is demonstrated by the workflow.

**Exit condition:** RAVEN can explain whether an answer came from stored evidence or newly acquired evidence, while keeping persistence application-owned.

## Day 5 — Evidence coverage and workspace quality

**Status: implemented on `feat/day5-enrichment-workspace`; release verification remains branch-local until Microsoft Edge completion checks and review pass.**

Delivered scope is coverage-aware source-root selection, generic corporate-family discovery, bounded target-aware official-site expansion, MaSoThue `BusinessDirectory` parsing, target-scoped profile patching, and company archive/delete/merge/workspace-review services. The final product slice adds the corresponding profile-enrichment, lifecycle, Investigations, Monitoring, and Ask RAVEN dock frontend workflows. Ask RAVEN backend ownership remains with Hung; this branch consumes no invented conversation contract.

## Stabilization principles

Prefer small feature branches, focused reviews, mockable provider tests, and working vertical slices. Do not add distributed infrastructure merely to simulate activity; recorded run state and staged HTTP operations are sufficient for the MVP.
