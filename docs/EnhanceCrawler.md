# Plan: Improve Crawl4AI Content Filtering Pipeline

## Context

Repository: `Sanguin3G/RAVEN`

Target branch:

```text
codex/integrate-web-search
```

Current research pipeline:

```text
Search
→ Crawl4AI Local
→ CrawlResult.Markdown
→ SourceDocument.Content
→ ProfileInputBuilder
→ Gemini
```

Current problem:

`Crawl4AiLocalProvider` sends:

```csharp
crawler_config = new { }
```

and reads:

```text
fit_markdown
→ fallback raw_markdown
```

into one `Markdown` field.

This causes two problems:

1. Crawled pages such as Wikipedia contain large amounts of navigation, menus, references and unrelated boilerplate.
2. If Crawl4AI filtering is enabled, `fit_markdown` may replace the original raw content, which weakens the current `SourceDocument` raw-evidence/provenance model.

RAVEN already has source-specific structured extraction for:

```text
TopCV
MaSoThue / BusinessDirectory
```

Do not replace those parsers.

---

# Objective

Improve crawling so RAVEN keeps:

```text
Raw content
→ audit / provenance / source-specific parsing

Filtered content
→ Gemini / profile generation
```

Target architecture:

```text
                    Web page
                       ↓
                   Crawl4AI
                       ↓
              ┌────────┴────────┐
              ↓                 ↓
       raw_markdown        fit_markdown
              ↓                 ↓
    SourceDocument.Content   FilteredContent
              ↓                 ↓
 TopCV/MaSoThue parser    ProfileInputBuilder
              ↓                 ↓
   StructuredFactsJson        Gemini
```

---

# Scope

Implement only:

1. Separate raw Markdown and filtered Markdown.
2. Enable Crawl4AI `PruningContentFilter`.
3. Persist filtered content separately.
4. Make profile generation prefer filtered content.
5. Preserve existing TopCV and MaSoThue structured-fact extraction.
6. Add fallback behavior.
7. Add/update tests.

Do NOT implement in this task:

```text
BM25ContentFilter
WikipediaSourceParser
RAG/chunking
embeddings
new LLM extraction pipeline
site-specific cleaners for official company websites
```

---

# Task 1 — Change CrawlResult contract

Inspect the current `CrawlResult` definition.

Replace the single-content concept with explicit fields similar to:

```csharp
string? RawMarkdown
string? FilteredMarkdown
```

Preserve the existing metadata:

```text
Provider
RequestedUrl
FinalUrl
Title
Success
Error
RetrievedAt
```

Compatibility rule:

```text
RawMarkdown should represent raw_markdown when available.

FilteredMarkdown should represent fit_markdown when available.
```

Do NOT silently overwrite raw content with filtered content.

If Crawl4AI returns Markdown as a simple string instead of the object format, treat it safely as raw content.

---

# Task 2 — Update Crawl4AiLocalProvider

File:

```text
backend/src/Raven.Api/Features/Crawling/Crawl4AiLocalProvider.cs
```

Currently the request uses:

```csharp
crawler_config = new { }
```

Configure Crawl4AI Markdown generation to use:

```text
PruningContentFilter
```

Use a conservative/default configuration.

Do not create aggressive thresholds that may remove short but important facts such as:

```text
address
employee count
CEO
tax number
company status
```

Expected Crawl4AI result:

```text
markdown.raw_markdown
markdown.fit_markdown
```

Parse both independently.

Do NOT use:

```text
FirstNonEmpty(fit_markdown, raw_markdown)
```

as the only output anymore.

---

# Task 3 — Safe fallback behavior

Crawler must still succeed when filtering is unavailable.

Desired logic:

```text
raw exists + fit exists
→ RawMarkdown = raw
→ FilteredMarkdown = fit

raw exists + fit missing
→ RawMarkdown = raw
→ FilteredMarkdown = null

raw missing + fit exists
→ RawMarkdown = fit
→ FilteredMarkdown = fit

both missing
→ crawl failure
```

Filtering failure must not make otherwise readable evidence unusable.

The raw document remains the fallback evidence.

---

# Task 4 — Extend SourceDocument

Inspect:

```text
backend/src/Raven.Api/Features/Research/SourceDocument.cs
```

Keep:

```csharp
Content
```

as the original/raw crawled evidence.

Add a nullable property such as:

```csharp
public string? FilteredContent { get; init; }
```

Meaning:

```text
Content         = raw evidence
FilteredContent = cleaned/fit Markdown optimized for downstream AI
```

Do not rename `Content` unless absolutely necessary because it is already used across the application.

Add the required EF Core configuration and migration.

Do not impose an unnecessarily small maximum length on `FilteredContent`.

---

# Task 5 — Update acquisition persistence

In:

```text
ResearchCompanyService.AcquireAsync
```

persist:

```text
Content = crawl.RawMarkdown
FilteredContent = crawl.FilteredMarkdown
```

Content hashing / deduplication should continue to use raw content so different filtering configurations do not make the same source appear as different documents.

Desired:

```csharp
HashContent(crawl.RawMarkdown)
```

rather than filtered content.

---

# Task 6 — Preserve StructuredFacts behavior

Current logic:

```csharp
SourceKind.TopCv
    → TopCvSourceParser

SourceKind.BusinessDirectory + MaSoThue
    → MaSoThueSourceParser
```

Keep this behavior.

Important:

Source-specific parsers should receive:

```text
RawMarkdown
```

not filtered content.

Reason:

Filtering may remove short labels or table values that deterministic parsers depend on.

Pipeline should therefore be:

```text
RawMarkdown
├─ TopCv parser
├─ MaSoThue parser
└─ persisted as Content

FilteredMarkdown
└─ profile-generation input
```

Do not remove or redesign `StructuredFactsJson`.

---

# Task 7 — Update ProfileInputBuilder

File:

```text
backend/src/Raven.Api/Features/Profiles/Generation/ProfileInputBuilder.cs
```

Currently it uses:

```csharp
document.Content
```

Change profile evidence content selection to:

```text
FilteredContent if meaningful
otherwise Content
```

Conceptually:

```csharp
var sourceContent =
    !string.IsNullOrWhiteSpace(document.FilteredContent)
        ? document.FilteredContent
        : document.Content;
```

Then continue using the existing:

```text
MaxCharactersPerSource
MaxTotalCharacters
CleanAndBoundContent
```

Do not remove the existing character limits.

Structured facts must continue to be passed separately to Gemini.

Final Gemini evidence should look conceptually like:

```text
SOURCE
    filtered textual evidence

STRUCTURED_FACTS
    deterministic TopCV/MaSoThue fields
```

---

# Task 8 — Do not add website-specific cleaners

Do NOT create classes such as:

```text
MicrosoftCleaner
FptCleaner
GoogleCleaner
OfficialWebsiteCleaner per company
```

Generic official websites should use:

```text
Crawl4AI
→ PruningContentFilter
→ filtered Markdown
→ Gemini
```

Source-specific parsers should only exist for sources with stable, reusable structure.

Existing examples:

```text
TopCV
MaSoThue
```

Wikipedia-specific parsing is outside this task.

---

# Task 9 — Add basic filtering telemetry

Add enough diagnostic information to evaluate filtering without storing large duplicated payloads in telemetry.

Where consistent with the existing telemetry architecture, capture values such as:

```text
rawCharacters
filteredCharacters
reductionRatio
filterApplied
filterFallbackUsed
```

Example:

```text
rawCharacters      = 501000
filteredCharacters = 82000
reductionRatio     = 0.836
filterApplied      = true
```

Do not persist raw page contents into telemetry.

Do not log credentials, cookies or provider payloads.

---

# Task 10 — Tests

Update existing Crawl4AI provider tests and add cases for:

### Case 1

```text
raw_markdown exists
fit_markdown exists
```

Expected:

```text
RawMarkdown == raw
FilteredMarkdown == fit
```

### Case 2

```text
raw_markdown exists
fit_markdown empty
```

Expected:

```text
RawMarkdown == raw
FilteredMarkdown == null
```

### Case 3

```text
fit_markdown exists
raw_markdown missing
```

Expected safe fallback.

### Case 4

Both missing.

Expected crawl failure.

### Case 5

ProfileInputBuilder with:

```text
Content = noisy raw content
FilteredContent = clean content
```

Expected Gemini evidence to contain clean content.

### Case 6

`FilteredContent = null`

Expected ProfileInputBuilder to fall back to `Content`.

### Case 7

TopCV structured facts.

Verify parser still receives usable raw Markdown and existing structured-fact behavior remains unchanged.

### Case 8

MaSoThue structured facts.

Verify the same behavior.

### Case 9

Deduplication.

Verify content hash remains based on raw content and is unaffected by different filtered output.

---

# Acceptance criteria

Implementation is complete when:

1. `SourceDocument.Content` still contains raw evidence.
2. Filtered Crawl4AI Markdown is persisted separately.
3. Profile generation prefers filtered content.
4. Raw content is used as fallback.
5. TopCV structured facts still work.
6. MaSoThue structured facts still work.
7. Deduplication continues using raw content.
8. Existing profile-generation character limits remain.
9. Existing tests pass.
10. New filtering/fallback tests pass.
11. No BM25 filtering is introduced.
12. No Wikipedia parser is introduced.
13. No company-specific website cleaner is introduced.

---

# Expected final pipeline

```text
Brave / Exa Search
        ↓
ResearchCandidate
        ↓
Crawl4AI Local
        ↓
PruningContentFilter
        ↓
┌────────────────────────────┐
│ raw_markdown               │
│                            │
│ → SourceDocument.Content   │
│ → dedup hash               │
│ → TopCV/MaSoThue parser    │
└────────────────────────────┘

┌────────────────────────────┐
│ fit_markdown               │
│                            │
│ → FilteredContent          │
│ → ProfileInputBuilder      │
│ → Gemini                   │
└────────────────────────────┘

StructuredFactsJson
        ↓
ProfileInputBuilder
        ↓
Gemini structured profile
```

## Implementation constraint

Before changing code, inspect the existing contracts, EF migrations, tests and provider response fixtures on `codex/integrate-web-search`.

Prefer the smallest change compatible with the existing architecture.

Do not refactor unrelated research, Deep Research, Chat, source ranking or profile-confirmation flows.
