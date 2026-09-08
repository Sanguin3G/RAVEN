# Data model

- **Company:** identity, website/country, timestamps, last researched time.
- **ResearchRun:** lifecycle, requested/actual search and crawler provider, model, counts, error.
- **SourceDocument:** URL and normalized URL, content hash, clean Markdown, retrieval metadata.
- **SourceChunk:** document/company references, ordered text, embedding, metadata.
- **CompanyProfileVersion:** immutable JSON profile snapshot and monotonically increasing version number.
- **ProfileEvidence:** maps a profile field path to its document/chunk and confidence.
- **ProfileChange:** structured old/new values between two profile versions.

Profiles are append-only. A successful refresh creates version N+1, maps evidence, diffs structured fields, and stores changes.
