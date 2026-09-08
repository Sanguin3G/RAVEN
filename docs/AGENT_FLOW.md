# Agent flow

Deep Research starts with a CompanyId-filtered internal knowledge search. If evidence is sufficient, it answers with citations from stored sources. Otherwise the CompanyResearchAgent may inspect the profile and sources, use search/crawl tools, then invoke configured Exa MCP or Firecrawl MCP capabilities.

MCP is not part of routine deterministic profile generation. Every final answer must be grounded in RAVEN-held or newly persisted evidence, and the UI must distinguish local evidence from externally refreshed evidence.
