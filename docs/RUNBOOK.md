# Runbook

## Prerequisites

Install .NET SDK 10, Node.js, Docker Desktop, and provider credentials as required. Copy `.env.example` to `.env`; never commit the latter.

## Local start

1. `docker compose up -d crawl4ai`
2. `cd backend; dotnet restore Raven.sln; dotnet run --project src/Raven.Api`
3. `cd frontend; npm install; npm run dev`

The API runs on its ASP.NET-selected local URL; confirm `/health`. The web server runs on port 5173.

## Troubleshooting

If Crawl4AI is unavailable, inspect `docker compose logs crawl4ai`. Do not silently substitute a cloud crawler: configuration and actual-provider reporting must make fallback explicit. Invalid credentials must fail visibly rather than trigger fallback.
