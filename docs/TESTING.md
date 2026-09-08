# Testing

Provider adapters are behind interfaces and tested with fixtures/mocks; normal CI must never need external API calls. Unit coverage targets URL normalization/ranking, source and content deduplication, identity validation, chunking, profile validation/diff, provider priority, fallback classification, and provider mapping.

Integration coverage will verify SQLite persistence, ResearchRun lifecycle, profile version/evidence persistence, CompanyId RAG filtering, API contracts, and fallback chains. A test-company suite runs across configured provider combinations without company-specific code.
