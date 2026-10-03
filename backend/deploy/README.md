# Portable deployment preparation

RAVEN ships two Linux container images usable on a Docker host, Railway, AWS ECS/EC2, Cloud Run, or another platform that supports these requirements. No cloud account or service URL is baked into either image. Deployment itself is deferred; CI validates startup and authentication using disposable containers.

## Build and configure

From the repository root:

```sh
docker build -t raven-api backend
docker build -t raven-frontend frontend
```

Both images listen on port **8080**. The API exposes `/health` and `/api/health`. The frontend serves `/` and SPA deep links, and proxies `/api` to its runtime `RAVEN_API_ORIGIN` (an absolute HTTP(S) origin with no trailing path). Chat SSE buffering is disabled and proxy timeouts are one hour; also configure the chosen platform's request timeout for the intended workflow.

Supply these values through the platform's secret/environment configuration:

| Service | Variable | Purpose |
| --- | --- | --- |
| API | `ASPNETCORE_ENVIRONMENT=Production` | Secure cookies and production behavior |
| API | `ConnectionStrings__Raven=Data Source=/app/data/raven.db` | SQLite file on the persistent mount |
| API | `RAVEN_DATA_PROTECTION_PATH=/app/data/keys` | Persist Identity/antiforgery cookie keys across restarts |
| API | `RAVEN_BOOTSTRAP_ADMIN_EMAIL`, `RAVEN_BOOTSTRAP_ADMIN_PASSWORD` | Create the first Admin only when no users exist |
| API | `RAVEN_CREDENTIAL_MASTER_KEY` | Base64-encoded 32 random bytes for encrypted workspace overrides |
| Frontend | `RAVEN_API_ORIGIN` | API origin reachable from the frontend container |

Provider fallbacks are optional: `BRAVE_SEARCH_API_KEY`, `EXA_API_KEY`, `GEMINI_API_KEY`, `GOOGLE_MAPS_EMBED_API_KEY`, `CRAWL4AI_LOCAL_BASE_URL`, and `CRAWL4AI_API_TOKEN`. Use environment bindings from any provider's secret manager; Google Secret Manager remains supported through those bindings. No secret is required in a frontend build. Maps is intentionally visible to authenticated browsers; restrict that key to Maps Embed API and the chosen frontend origin.

## Storage, TLS, and process requirements

- Run **one API replica**. Mount persistent storage at `/app/data` and allow the container's .NET `app` user (UID 1654) to write there. Railway can use a volume; AWS can use a persistent disk on a single host or an appropriate single-writer block-backed container volume. Do not put SQLite on a shared network filesystem or claim horizontal scaling support.
- Protect the volume and its backups: they contain business data, Identity password hashes, encrypted overrides, and Data Protection keys. Keep the credential master key separately in the deployment secret manager. Preserve that same key when moving the workspace.
- Terminate **HTTPS** at an ingress/load balancer in front of the frontend. Production cookies use `Secure`; a browser on plain HTTP cannot log in. Keep the API private behind the frontend. When the ingress/proxy supplies trusted forwarded headers, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` on the API. This switch trusts forwarding headers, so the API must not be reachable directly from untrusted clients. The ingress must overwrite client-supplied forwarding headers.
- Keep the API process awake with CPU available for in-process research/monitoring workers. Disable scale-to-zero for long workflows or present those locally. Cloud Run request-based CPU and temporary filesystems retain their documented limitations.
- No fixed public domain is required. Configure the frontend origin, HTTPS, provider restrictions, and proxy routing when choosing the host.

## Empty workspace or curated evaluation seed

Default startup creates a fresh database through EF migrations and bootstraps the configured Admin. It does **not** load Company demo data.

For a resettable evaluator sandbox, set `RAVEN_SEED_DATABASE=true`. The API copies bundled `seed/raven.seed.db` only if the runtime DB is absent, then applies migrations and bootstraps users. Set `RAVEN_DEMO_MODE=true` independently if changes may reset. Existing runtime data is never replaced. The curated seed has seven approved Companies, no accounts, and no provider overrides. Each export is vacuumed to remove deleted data from SQLite free pages. An ephemeral hosting filesystem is suitable only for this resettable mode, not durable production storage.

## Docker Compose reference

`backend/deploy/compose.yaml` is a portable reference for a single host. Export the three required Admin/master-key values and provider settings into the shell, then run from the repository root:

```sh
docker compose -f backend/deploy/compose.yaml up -d --build
```

It binds the frontend to loopback port 8080 for a local HTTPS ingress and keeps the API internal. The named volume persists both SQLite and cookie keys. Default Crawl4AI is the host's port 11235; override its endpoint/token for a container or remote service. This Compose file does not provision a domain, TLS ingress, or cloud resources.

## Optional private Cloud Run crawler

The existing private crawler integration remains available on Google hosting: set `CRAWL4AI_CLOUD_RUN_AUDIENCE` and the crawler base URL, grant the API service account Cloud Run Invoker, and keep Crawl4AI's own token separately. The metadata identity token uses `X-Serverless-Authorization`; Crawl4AI's Bearer token uses `Authorization`. Leave the audience unset on other hosts so no Google metadata call is attempted. Calling that private IAM-only service from another cloud requires an operator-supplied identity arrangement; the token handler does not implement cross-cloud federation.

## Verification and backup

CI builds both images and runs `python3 backend/deploy/container_smoke.py`: fresh and seeded API startup, anonymous 401, HTTPS proxy login, SPA deep link, and session/data preservation after API restart. It uses random temporary credentials and never calls paid providers. For a local repeat, build images as `raven-api-ci` and `raven-frontend-ci` first; the script also needs Python 3 and OpenSSL.

After a future deployment, verify HTTPS login/logout, Researcher restrictions, one dossier, request-bound Chat/SSE, provider status, and crawler recovery. These live checks are operator acceptance steps for the selected host, not prerequisites for merging preparation code.

For a backup, stop the API instance, copy the mounted `/app/data` directory (database plus any SQLite journal sidecars and `keys/`) into a protected backup location, then start the same instance. Back up the credential master key separately. For example, on a single host with a bind mount, `cp -a /srv/raven/data /srv/raven/backups/data-YYYYMMDD` while the API is stopped. Restore into an empty writable data directory with the original master key. Do not overwrite a running database.
