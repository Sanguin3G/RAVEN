# Controlled Gemini identity probe

This is a non-production, identity-only Day-6 preparation tool. It calls the
existing `Raven.Api.Features.Ai.IAiModelProvider` boundary backed by the
existing `GeminiProvider`; it does not register with the API, create a
`Company`, create a `ResearchRun`, call Search/Crawl, or send source evidence.

The tool loads the existing Raven.Api user-secrets ID and environment-variable
overrides. It defaults to `gemini-3.5-flash-lite`, while allowing the existing
`GEMINI_IDENTITY_MODEL`, `GEMINI_FAST_MODEL`, or `Providers:Gemini:FastModel`
configuration values to select a configured fast model.

From `backend`, configure the existing API user secret if needed:

```powershell
dotnet user-secrets set GEMINI_API_KEY '<key>' --project src/Raven.Api
dotnet run --project tools/IdentityProbe/IdentityProbe.csproj --no-restore
```

`GEMINI_API_KEY` may also be supplied for a one-off run. The output is a
sanitized line per identity hint with semantic status, ambiguity type, bounded
entity summaries, model, duration, token usage (when returned), structured
parse success, and a safe failure code. It never prints the API key, raw model
JSON, prompts, or hidden reasoning.
