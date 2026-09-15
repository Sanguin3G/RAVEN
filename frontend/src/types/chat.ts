export type ChatAnswerStatus = "answered" | "clarificationRequired" | "insufficientEvidence" | "unsupportedScope";
export type ChatMessageRole = "user" | "assistant";
export type ChatMessageStatus = "pending" | "completed" | "failed";

export interface ChatCitation {
  origin: "profile";
  sourceDocumentId: string;
  fieldPath?: string | null;
  title?: string | null;
  url: string;
  retrievedAt: string;
}

export interface ChatToolExecution {
  tool: "get_source_excerpt";
  provider: string;
  status: string;
  durationMs: number;
  errorCode?: string | null;
}

export interface ChatMessage {
  id: string;
  role: ChatMessageRole;
  content: string;
  status: ChatMessageStatus;
  answerStatus?: ChatAnswerStatus | null;
  followUpQuestion?: string | null;
  citations: ChatCitation[];
  toolExecutions: ChatToolExecution[];
  createdAt: string;
}

export interface ChatConversationResponse {
  id: string;
  companyId: string;
  profileVersionId: string;
  profileVersion: number;
  title: string | null;
  createdAt: string;
  updatedAt: string;
  messages: ChatMessage[];
}

export interface SendChatMessageRequest {
  question: string;
}

export interface SendChatMessageResponse {
  conversationId: string;
  messageId: string;
  companyId: string;
  profileVersion: number;
  status: ChatAnswerStatus;
  answer: string;
  citations: ChatCitation[];
  toolExecutions: ChatToolExecution[];
  followUpQuestion?: string | null;
}
