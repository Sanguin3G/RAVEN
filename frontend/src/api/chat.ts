import { request } from "./client";
import type { ChatConversationResponse, SendChatMessageRequest, SendChatMessageResponse } from "../types/chat";

function companyPath(companyId: string) {
  return `/api/companies/${encodeURIComponent(companyId)}/chat/conversations`;
}

export function createChatConversation(companyId: string) {
  return request<ChatConversationResponse>(companyPath(companyId), { method: "POST" });
}

export function getChatConversation(companyId: string, conversationId: string) {
  return request<ChatConversationResponse>(`${companyPath(companyId)}/${encodeURIComponent(conversationId)}`);
}

export function sendChatMessage(companyId: string, conversationId: string, payload: SendChatMessageRequest) {
  return request<SendChatMessageResponse>(`${companyPath(companyId)}/${encodeURIComponent(conversationId)}/messages`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
}
