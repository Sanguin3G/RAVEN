# Ask RAVEN — Profile Chat V1

## Scope

V1 trả lời câu hỏi về công ty đang mở bằng accepted `CompanyProfileVersion` đã được pin vào conversation. Profile 1–2 trang được gửi đầy đủ vào Gemini Structured Output prompt. V1 không tìm kiếm/crawl web, không Graph DB và không MCP.

## Runtime flow

```text
POST /api/companies/{companyId}/chat/conversations
  -> kiểm tra company + accepted profile
  -> tạo conversation, pin ProfileVersionId

POST .../conversations/{conversationId}/messages
  -> normalize + validate request
  -> kiểm tra conversation thuộc company
  -> load pinned profile + evidence + tối đa 10 message gần nhất
  -> Gemini Structured Output
       final                 -> validate answer/status/citations
       get_source_excerpt    -> tool đọc SourceDocument đã được profile link
       (tối đa 3 round, 2 lần đọc excerpt)
  -> persist user/assistant message, citation, tool telemetry
  -> trả response
```

## Structured Output contract

```json
{
  "action": "final | get_source_excerpt",
  "status": "answered | clarification_required | insufficient_evidence | unsupported_scope",
  "answer": "string",
  "sourceDocumentId": "guid | null",
  "citedSourceDocumentIds": ["guid"],
  "followUpQuestion": "string | null"
}
```

Gemini chỉ phân tích và chọn bước tiếp theo. Backend là authority cho company isolation, profile pinning, citation whitelist, tool budget và persistence.

## Main components

- `Features/Chat/CompanyChatService.cs`: application orchestration, conversation lifecycle, validation và persistence.
- `Features/Chat/CompanyChatAgent.cs`: Gemini structured decision loop qua `IAiModelProvider`; không phụ thuộc SDK Gemini.
- `Features/Chat/ChatEvidenceTool.cs`: read-only excerpt tool, chỉ đọc source ID nằm trong profile evidence và cùng CompanyId.
- `Features/Chat/ChatContracts.cs`: HTTP DTO, agent contract và trạng thái trả lời.
- `Data/RavenDbContext.cs` + `Data/Migrations/20260915120000_AddProfileChatV1.cs`: persistence schema.
- `frontend/src/components/dossier/AskRavenHandoff.tsx`: composer và conversation UI profile-only.

## Implementation plan

1. Hoàn tất persistence: conversation pin, messages, citations, tool execution telemetry và migration.
2. Hoàn tất agent boundary: structured schema, prompt, bounded source-excerpt loop, provider-neutral AI interface.
3. Hoàn tất backend validation: route isolation, profile evidence whitelist, invalid-output handling, timeout và safe failed message.
4. Hoàn tất UI/API contract: tạo conversation lazy, gửi question, hiển thị status/citations, không có mode web/deep giả.
5. Regression: backend integration tests, frontend component test, build và migration smoke test.

## V2 extension points

External company lookup, Graph DB, web search/crawl và MCP chỉ thêm sau khi có scope riêng. Khi đó mở rộng bằng tool registry/policy và evidence provenance; không đưa vào profile-only agent V1.
