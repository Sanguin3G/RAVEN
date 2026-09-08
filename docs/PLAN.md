# RAVEN Implementation Plan
This is a direction-setting plan for a two-person team over approximately 20 working days. GitHub Issues own granular work; [STATUS.md](STATUS.md) records active execution.

## Roles

**Person A — Backend / AI primary** owns the API, persistence, provider adapters, research orchestration, and retrieval services. **Person B — Frontend / Product primary** owns the React experience and presentation of profiles, sources, history, and answers.

Both collaborators may work outside their primary area. Shared responsibility covers DTO/API contracts, architecture decisions, integration, testing, and the final demo.

## M1 — Core Research

**Target: approximately Day 5**

~~~text
Company → Brave → Crawl4AI Local → Gemini → Company Profile → React
~~~

Person A primarily delivers ASP.NET and SQLite foundation, Brave search, Crawl4AI integration, Gemini integration, and the deterministic research workflow. Person B primarily delivers the React foundation, company create/list flow, research progress UX, profile display, and source UX.

**Exit condition:** a real company can be researched end-to-end and shown in React with supporting sources.

## M2 — Company Knowledge

**Target: approximately Day 11**

Implement persistent sources, ResearchRun, immutable profile versions, refresh, change-tracking foundations, RAG, and Ask Company. Person A primarily owns storage, embeddings, retrieval, and backend endpoints. Person B primarily owns Company Detail, Sources, History, Changes, and Ask Company screens.

**Exit condition:** researched company knowledge survives restart, can be questioned with citations, and can be refreshed without losing historical evidence or profiles.

## M3 — Complete RAVEN

**Target: approximately Day 17**

Implement Agent Framework, Deep Research, Exa MCP, additional retrieval providers, provider settings, and fallback. Parallel Luna implementation is encouraged here because provider adapters have naturally separate scopes.

**Exit condition:** Deep Research is RAG-first and may acquire external evidence when needed; balanced, Easy Cloud, and Cloud Robust configurations are usable.

## Stabilization

**Days 18–20**

Work only on bugs, relevant tests, failure handling, Docker Compose, UX polish, documentation updates, and demo preparation. Avoid major architecture changes in this phase.

**Final exit condition:** a fresh environment can demonstrate research, evidence, RAG, Deep Research, provider configuration, and tracking.
