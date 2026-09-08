# API

Planned surface:

```text
GET/POST  /api/companies
GET/DELETE /api/companies/{id}
POST      /api/companies/{id}/research
POST      /api/companies/{id}/refresh
GET       /api/companies/{id}/profile
GET       /api/companies/{id}/profile/history
GET       /api/companies/{id}/sources
GET       /api/companies/{id}/changes
GET       /api/companies/{id}/research-runs
GET       /api/research-runs/{id}
POST      /api/companies/{id}/ask
POST      /api/companies/{id}/deep-research
POST      /api/intelligence/ask
GET/PUT   /api/settings/providers
POST      /api/settings/providers/{id}/test
```

Current operational endpoints are `GET /api` and `GET /health`.
