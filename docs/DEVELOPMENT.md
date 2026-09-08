# RAVEN Development
## Requirements

Install the .NET 10 SDK, Node.js, Docker Desktop, and Git. Provider credentials are needed only after their adapters are implemented.

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

The frontend development server uses port 5173. The API uses the ASP.NET-selected local URL and currently exposes GET /api and GET /health.

## Configuration and secrets

.env.example lists planned provider variable names: BRAVE_SEARCH_API_KEY, EXA_API_KEY, CRAWL4AI_CLOUD_API_KEY, FIRECRAWL_API_KEY, GEMINI_API_KEY, OPENAI_COMPATIBLE_BASE_URL, OPENAI_COMPATIBLE_API_KEY, OPENAI_COMPATIBLE_MODEL, RAVEN_CONNECTION_STRING, and CRAWL4AI_LOCAL_BASE_URL.

Never commit a populated .env. At present, no provider adapters or .env loading mechanism exist, so these values are placeholders rather than active configuration. The API currently reads its SQLite connection string from appsettings.json or the standard ASP.NET Core ConnectionStrings__Raven environment variable.

## Docker

docker-compose.yml starts Crawl4AI Local as unclecode/crawl4ai:latest, publishing port 11235. Inspect it with:

~~~powershell
docker compose logs crawl4ai
~~~

Do not silently substitute a cloud crawler when the local service fails. Future fallback must be configured and reported to the user.

## Database

SQLite is the current database. Running the API calls EF Core EnsureCreatedAsync; there are no EF migrations yet. With the documented backend startup command, the default database file is backend/raven.db. When migrations are introduced, document their exact commands here and treat EF models/migrations as the detailed schema authority.

## Tests and checks

No backend or frontend test project is registered yet. Current useful checks are:

~~~powershell
# from backend
dotnet build Raven.sln

# from frontend
npm run build
~~~

When test projects are added, record their exact commands here. Provider adapters must be tested through mocks or fixtures; normal automated tests must not require paid services or real credentials.

## Git workflow

Use main, short-lived feature branches, small pull requests, and GitHub Issues. Prefer feat/<issue>-short-name and fix/<issue>-short-name. A pull request normally corresponds to one logical issue. Coordinate before editing shared contracts, Program.cs, frontend bootstrap/routing, Docker Compose, EF migration ordering, or STATUS.md.

## Codex workflow

Terra handles planning, parallel task decomposition, integration review, and STATUS.md updates. Luna agents handle focused implementation tasks. Keep task ownership narrow and expose registration helpers rather than directly changing shared integration hotspots.
