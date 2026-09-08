# Architecture

RAVEN separates **discovery**, **reading**, and **reasoning**. A search provider returns candidate URLs; a crawler provider turns selected URLs into clean documents; application-owned workflows normalize and persist them. Models consume selected evidence, never an unbounded crawl dump.

Fast Research is deterministic: Company → search → rank/filter → crawl → persist → chunk/embed → retrieve evidence → structured profile → version/diff. Deep Research is agentic, but queries internal RAG first and goes to external tools only when stored evidence is insufficient.

SQLite is the source of record for domain data, run history, sources, profile versions, evidence, changes, configuration, conversations, and practical vector metadata.
