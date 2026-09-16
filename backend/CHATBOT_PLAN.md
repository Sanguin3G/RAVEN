# Ask RAVEN — Profile Chat V1

## Implemented boundary

Ask RAVEN is persistent, company-scoped Chat over the accepted
`CompanyProfileVersion` pinned to each conversation. It does not search or
crawl the web and it does not invoke Deep Research implicitly.

```text
POST /api/companies/{companyId}/chat/conversations
  -> verify Company and accepted profile
  -> create conversation pinned to ProfileVersionId

POST .../conversations/{conversationId}/messages
  -> validate company/conversation isolation
  -> load pinned profile, evidence, and bounded recent history
  -> apply safe conversational/guidance policy when applicable
  -> otherwise use Gemini structured output with a bounded source-excerpt tool
  -> validate citations for factual answers
  -> persist messages, citations, and tool executions
```

## Response semantics

- `Answered`: factual company content; citations are required.
- `Conversational`: greetings, thanks, and simple dialogue; no invented company facts or citations.
- `Guidance`: product/navigation help, including the Deep Research/Investigations handoff; no invented company facts or citations.
- `ClarificationRequired`, `InsufficientEvidence`, and `UnsupportedScope`: truthful bounded outcomes.

The backend remains authoritative for profile pinning, Company isolation,
citation whitelisting, excerpt-tool budget, and persistence. The model cannot
return arbitrary navigation URLs.

## Components

- `Features/Chat/CompanyChatService.cs`: conversation lifecycle, validation, and persistence.
- `Features/Chat/ChatAgent.cs`: provider-neutral structured-answer loop through `IAiModelProvider`.
- `Features/Chat/ChatConversationPolicy.cs`: safe citation-free conversational and guidance responses.
- `Features/Chat/ChatEvidenceTool.cs`: read-only, bounded excerpts from profile-linked `SourceDocument` records.
- `Features/Chat/ChatContracts.cs`: HTTP DTOs and answer statuses.
- `Data/Migrations/20260915120000_AddProfileChatV1.cs`: Chat persistence schema.
- `frontend/src/components/dossier/AskRavenHandoff.tsx`: conversation dock and capability menu.

## Product direction

Ask RAVEN is Chat. It may use accepted-profile evidence, stored conversation
history, and future explicitly enabled per-turn capabilities. The Day-7
composer exposes a real **Open Investigations** handoff and a disabled
**Search the web** slot labelled as a Day-8 capability.

Deep Research is a distinct long-running workflow with persisted progress and
its own result/investigation surface. It is not a Chat mode. “Quick Research”
is retired product terminology.

## Deliberately deferred

- Actual web-enabled Chat turns and online-source citations.
- Rendering or launching a Deep Research run inside the conversation.
- Richer typed next-best actions beyond the current trusted Investigations handoff.
- RAG, embeddings, Graph DB, MCP, and unrestricted crawler access.
