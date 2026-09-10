# RAVEN Development
## Requirements

Install the .NET 10 SDK, Node.js, Docker Engine (Docker Desktop or a WSL distribution), and Git. Provider credentials are needed only after their adapters are implemented.

## Local startup

From the repository root, start the local crawler:

~~~powershell
docker compose up -d crawl4ai
~~~

Start the API from backend:

~~~powershell
dotnet restore Raven.sln
dotnet run --project src/Raven.Api
~~~

Start the frontend from frontend:

~~~powershell
npm install
npm run dev
~~~

The frontend development server uses port 5173. The API listens on `http://localhost:5180` in development and exposes Company endpoints, `POST /api/companies/{id}/research`, `GET /api/research-runs/{id}`, `GET /api/companies/{id}/sources`, `GET /api/sources/{id}`, `GET /health`, `GET /api/system/crawler-status`, and OpenAPI at `/openapi/v1.json`.

## Configuration and secrets

.env.example lists planned provider variable names: BRAVE_SEARCH_API_KEY, EXA_API_KEY, CRAWL4AI_CLOUD_API_KEY, FIRECRAWL_API_KEY, GEMINI_API_KEY, OPENAI_COMPATIBLE_BASE_URL, OPENAI_COMPATIBLE_API_KEY, OPENAI_COMPATIBLE_MODEL, RAVEN_CONNECTION_STRING, CRAWL4AI_LOCAL_BASE_URL, and CRAWL4AI_API_TOKEN.

Never commit a populated `.env`. RAVEN does not load `.env` files, so export provider values into the API process or use user secrets. The Brave adapter reads `BRAVE_SEARCH_API_KEY` (or `Providers__Brave__ApiKey`); Crawl4AI Local reads `CRAWL4AI_LOCAL_BASE_URL` and `CRAWL4AI_API_TOKEN` (or the `Crawl4AI__Local` configuration section). The API reads its SQLite connection string from appsettings.json or the standard ASP.NET Core `ConnectionStrings__Raven` environment variable.

## Docker

docker-compose.yml starts Crawl4AI Local as unclecode/crawl4ai:latest. In WSL development it publishes port 11235 on the Debian virtual interface so WSL localhost forwarding exposes it as `http://127.0.0.1:11235` to Windows; WSL NAT keeps it off the physical LAN by default. Current Crawl4AI images require `CRAWL4AI_API_TOKEN` to listen beyond the container loopback interface; Compose supplies a development-only default. For local crawling, pass the same value to the API process; production must use a distinct secret. The `crawl4ai-local` adapter sends it as a bearer token to `POST /crawl`. Inspect the container with:

~~~powershell
docker compose logs crawl4ai
~~~

Do not silently substitute a cloud crawler when the local service fails. Future fallback must be configured and reported to the user.

## Database

SQLite is the current database. API startup applies EF Core migrations. With the documented backend startup command, the default database file is backend/src/Raven.Api/raven.db. Create and apply migrations from backend with:

~~~powershell
dotnet ef migrations add <MigrationName> --project src/Raven.Api --startup-project src/Raven.Api
dotnet ef database update --project src/Raven.Api --startup-project src/Raven.Api
~~~

EF models and migrations are the detailed schema authority.

## Tests and checks

A backend integration-test project is registered. Current checks are:

~~~powershell
# from backend
dotnet build Raven.sln
dotnet test Raven.sln

# from frontend
npm run build
~~~

When test projects are added, record their exact commands here. Provider adapters must be tested through mocks or fixtures; normal automated tests must not require paid services or real credentials.

## Git workflow

Use main, short-lived feature branches, small pull requests, and GitHub Issues. Prefer feat/<issue>-short-name and fix/<issue>-short-name. A pull request normally corresponds to one logical issue. Coordinate before editing shared contracts, Program.cs, frontend bootstrap/routing, Docker Compose, EF migration ordering, or STATUS.md.

## Codex workflow

Terra handles planning, parallel task decomposition, integration review, and STATUS.md updates. Luna agents handle focused implementation tasks. Keep task ownership narrow and expose registration helpers rather than directly changing shared integration hotspots.
