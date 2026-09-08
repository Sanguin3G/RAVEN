# Providers

| Provider | Search | Crawl | MCP | Role |
| --- | ---: | ---: | ---: | --- |
| Brave | Yes | No | No | Recommended default discovery |
| Crawl4AI Local | No | Yes | No | Recommended local crawler |
| Crawl4AI Cloud | Yes | Yes | — | Cloud fallback / easy preset |
| Exa | Yes | Content retrieval | Yes | Semantic discovery and MCP |
| Firecrawl | Yes | Yes | Yes | Robust cloud retrieval |
| Gemini | — | — | — | Primary AI family |
| OpenAI-compatible | — | — | — | Alternative AI |

**Balanced preset:** Brave → Exa → Crawl4AI Cloud → Firecrawl for search; Crawl4AI Local → Crawl4AI Cloud → Firecrawl for crawl. Other presets are Easy Cloud (Crawl4AI Cloud/Gemini), Cloud Robust (Exa/Firecrawl/Gemini), Local First (Brave/Crawl4AI Local), and custom ordering.

Fallback only for timeout, 429, temporary outage, 5xx, retryable network error, or failed retrieval. Do not mask invalid keys/configuration, authentication failures, bad input, or unsupported capability. Record actual provider and expose fallback to the user. Health checks validate configuration and connectivity without assuming vendor pricing or quota.
