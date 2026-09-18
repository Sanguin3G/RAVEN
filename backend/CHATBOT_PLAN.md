# Ask RAVEN — Profile Chat V1

## Implemented boundary

Ask RAVEN is persistent, company-scoped Chat over the accepted
`CompanyProfileVersion` pinned to each conversation. Each conversation persists a
user-controlled Web Search permission, defaulting to off; enabling permission
never itself invokes a provider. When enabled, a bounded agent may decide to
Search/Crawl using the normal provider interfaces and keeps acquired evidence
message-scoped.

```text
POST /api/companies/{companyId}/chat/conversations
  -> verify Company and accepted profile
  -> create conversation pinned to ProfileVersionId

PATCH .../conversations/{conversationId}/capabilities
  -> validate company/conversation isolation
  -> persist { webSearchEnabled } for future turns

GET .../conversations/{conversationId}
  -> return profile citations plus any message-scoped Web Evidence Snapshots

POST .../conversations/{conversationId}/messages
  -> validate company/conversation isolation
  -> load pinned profile, evidence, and bounded recent history
  -> apply safe conversational/guidance policy when applicable
  -> otherwise use Gemini structured output with a bounded source-excerpt tool
  -> validate citations for factual answers
  -> persist messages, citations, and tool executions

POST .../messages/stream
  -> use the same service/persistence path as /messages
  -> emit sanitized SSE stages only while actual work occurs
  -> emit one completed response or a failed event
```

## Response semantics

- `Answered`: factual company content; citations are required.
- `Conversational`: greetings, thanks, and simple dialogue; no invented company facts or citations.
- `Guidance`: product/navigation help, including the Investigations handoff; no invented company facts or citations.
- `ClarificationRequired`, `InsufficientEvidence`, and `UnsupportedScope`: truthful bounded outcomes.

The backend remains authoritative for profile pinning, Company isolation,
citation whitelisting, excerpt-tool budget, and persistence. The model cannot
return arbitrary navigation URLs.

Web Evidence Snapshots are bounded copies of a future Search/Crawl result,
owned by one Chat message. They preserve URL, normalized URL, rank, provider,
retrieval time, snippet, and excerpt. A Web citation points to a snapshot;
neither object is a `SourceDocument`, `ProfileEvidence`, or accepted profile
fact. Database constraints require every citation to reference exactly one
profile source or one Web snapshot.

## Components

- `Features/Chat/CompanyChatService.cs`: conversation lifecycle, validation, and persistence.
- `Features/Chat/ChatAgent.cs`: provider-neutral structured-answer loop through `IAiModelProvider`.
- `Features/Chat/ChatConversationPolicy.cs`: safe citation-free conversational and guidance responses.
- `Features/Chat/ChatEvidenceTool.cs`: read-only, bounded excerpts from profile-linked `SourceDocument` records.
- `Features/Chat/ChatWebSearchPolicy.cs`: deterministic freshness/explicit-web guardrail. Normal questions remain optional; current or explicit web requests require Web evidence when permission is on, and clearly report that permission is needed when it is off.
- `Features/Chat/ChatWebSearchReranker.cs` and `ChatEvidenceReranker.cs`: deterministic Chat-only result scoring, URL/domain de-duplication, bounded per-domain selection, and relevant crawled passages.
- `Features/Chat/ChatWebEvidenceSnapshot.cs` (entity in `ChatEntities.cs`): bounded, message-scoped future web evidence.
- `Features/Chat/ChatWebTool.cs`: request-scoped Search/Crawl seam; at most two Searches, five ranked candidates per Search, and three Crawls. It only accepts candidate IDs produced by its own Search call.
  It filters literal localhost/private-IP candidates and rejects unsafe crawler final URLs before evidence persistence. It uses the Chat-only reranker, so Company Profile Research source selection is not reused.
- `Features/Chat/ChatAgent.cs`: bounded ReAct loop. It exposes Web actions only when the conversation permission is enabled; a factual final answer can cite only accepted-profile sources or Web drafts retrieved in the same turn.
- `Features/Chat/ChatContracts.cs`: HTTP DTOs, answer statuses, and progress stages.
- `Features/Chat/ChatSseProgressReporter.cs`: per-request SSE writer; it never persists heartbeats.
- `Data/Migrations/20260915120000_AddProfileChatV1.cs`: Chat persistence schema.
- `frontend/src/components/dossier/AskRavenHandoff.tsx`: conversation dock and capability menu.

## Product direction

Ask RAVEN is Chat. It may use accepted-profile evidence, stored conversation
history, and the explicitly enabled bounded Web Search capability. Search/Crawl
remains optional per question, but actual provider calls emit sanitized SSE stages
(`Analyzing`, `CheckingProfile`, `WebSearching`, `Crawling`, `Composing`) and
a final `completed` or `failed` event. The JSON message endpoint remains a fallback.

Each factual assistant answer displays `Profile`, `Web`, or `Mixed` from its validated citation origins. The badge is derived on read/render rather than trusted as model output or stored as accepted profile truth.

Saved Investigations are reference material with their own workspace surface;
they are not a Chat mode. “Quick Research” is retired product terminology.

## Deliberately deferred

- Richer typed next-best actions beyond the current trusted Investigations handoff.
- RAG, embeddings, Graph DB, MCP, and unrestricted crawler access.
