# Status

**Last updated:** 2026-09-08

**Current milestone:** Phase Zero / Foundation initialization

## Working

- Repository, root configuration, Docker Compose, and environment template
- ASP.NET Core API bootstraps with SQLite and health endpoint
- React/TypeScript/Vite shell and declared routes
- Crawl4AI Local service definition

## Partial

- Domain model: only `Company` is represented in the initial DbContext.
- API and UI routes are placeholders, not product functionality.

## Broken / blocked

- No provider credentials or provider implementations are configured.
- Packages have not yet been restored in this checkout.

## Provider support

| Capability | Provider | State |
| --- | --- | --- |
| Search | Brave, Exa, Crawl4AI Cloud, Firecrawl | Not Started |
| Crawl | Crawl4AI Local, Crawl4AI Cloud, Firecrawl | Compose only / Not Started |
| Models | Gemini, OpenAI-compatible | Not Started |
| MCP | Exa MCP, Firecrawl MCP | Not Started |

## Tests

No test project yet. The next foundation task creates test infrastructure before provider integration.

## Next integration targets

1. Company API and migrations
2. Search/crawler contracts and provider configuration
3. Sacred vertical slice with mocked external dependencies
