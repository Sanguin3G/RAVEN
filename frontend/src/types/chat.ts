export type ChatAnswerStatus = "Answered" | "Conversational" | "Guidance" | "ClarificationRequired" | "InsufficientEvidence" | "UnsupportedScope";
export type ChatMessageRole = "User" | "Assistant";
export type ChatMessageStatus = "Pending" | "Completed" | "Failed";

export interface ChatCitation {
  origin: "Profile" | "Web";
  sourceDocumentId?: string | null;
  webEvidenceSnapshotId?: string | null;
  fieldPath?: string | null;
  title?: string | null;
  url: string;
  retrievedAt: string;
}

export interface ChatWebEvidenceSnapshot {
  id: string;
  url: string;
  title?: string | null;
  searchSnippet?: string | null;
  contentExcerpt: string;
  searchProvider: string;
  crawlerProvider?: string | null;
  searchRank: number;
  retrievedAt: string;
}

export interface ChatToolExecution {
  tool: string;
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
  activity?: string | null;
  citations: ChatCitation[];
  webEvidenceSnapshots: ChatWebEvidenceSnapshot[];
  toolExecutions: ChatToolExecution[];
  createdAt: string;
}

export interface ChatConversationResponse {
  id: string;
  companyId: string;
  profileVersionId: string;
  profileVersion: number;
  webSearchEnabled: boolean;
  title: string | null;
  createdAt: string;
  updatedAt: string;
  messages: ChatMessage[];
}

export interface UpdateChatCapabilitiesRequest {
  webSearchEnabled: boolean;
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
  webEvidenceSnapshots: ChatWebEvidenceSnapshot[];
  toolExecutions: ChatToolExecution[];
  followUpQuestion?: string | null;
}
