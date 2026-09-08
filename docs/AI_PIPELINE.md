# AI pipeline

Fast Research is code-orchestrated and must be repeatable. It creates a ResearchRun, resolves identity, generates targeted queries, searches, selects about 5–8 high-value pages, crawls, normalizes/persists sources, chunks/embeds, retrieves field-specific evidence, generates a validated structured profile, maps evidence, versions the profile, and detects changes.

The profile model must use supplied evidence only: missing scalar facts are `null`; missing collections are `[]`. Model identifiers stay configuration-driven. Gemini Flash-Lite is the default fast-model family; an OpenAI-compatible adapter is the alternative.
