import { ApiError, getApiUrl, request, type ProblemDetails } from "./client";
import type { ChatConversationResponse, SendChatMessageRequest, SendChatMessageResponse, UpdateChatCapabilitiesRequest } from "../types/chat";

function companyPath(companyId: string) {
  return `/api/companies/${encodeURIComponent(companyId)}/chat/conversations`;
}

export function createChatConversation(companyId: string) {
  return request<ChatConversationResponse>(companyPath(companyId), { method: "POST" });
}

export function getChatConversation(companyId: string, conversationId: string) {
  return request<ChatConversationResponse>(`${companyPath(companyId)}/${encodeURIComponent(conversationId)}`);
}

export function updateChatCapabilities(companyId: string, conversationId: string, payload: UpdateChatCapabilitiesRequest) {
  return request<ChatConversationResponse>(`${companyPath(companyId)}/${encodeURIComponent(conversationId)}/capabilities`, {
    method: "PATCH",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
}

export function sendChatMessage(companyId: string, conversationId: string, payload: SendChatMessageRequest) {
  return request<SendChatMessageResponse>(`${companyPath(companyId)}/${encodeURIComponent(conversationId)}/messages`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
}

export type ChatStreamEvent =
  | { type: "progress"; stage: string; message: string; completed?: number | null; total?: number | null }
  | { type: "completed"; response: SendChatMessageResponse }
  | { type: "failed"; code: string; message: string };

export async function sendChatMessageStream(
  companyId: string,
  conversationId: string,
  payload: SendChatMessageRequest,
  onEvent: (event: ChatStreamEvent) => void,
  signal?: AbortSignal,
) {
  const response = await fetch(getApiUrl(`${companyPath(companyId)}/${encodeURIComponent(conversationId)}/messages/stream`), {
    method: "POST",
    headers: { Accept: "text/event-stream", "Content-Type": "application/json" },
    body: JSON.stringify(payload),
    signal,
  });
  if (!response.ok) {
    let problem: ProblemDetails | undefined;
    try { problem = await response.json() as ProblemDetails; } catch { /* streamed failures use an SSE event. */ }
    throw new ApiError(response.status, problem);
  }
  if (!response.body) throw new ApiError(response.status);

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  const dispatch = (frame: string) => {
    const eventName = frame.match(/^event:\s*(.+)$/m)?.[1]?.trim();
    const data = frame.match(/^data:\s*(.+)$/m)?.[1];
    if (!eventName || !data) return;
    const parsed = JSON.parse(data) as Record<string, unknown>;
    if (eventName === "progress") onEvent({ type: "progress", stage: String(parsed.stage), message: String(parsed.message), completed: typeof parsed.completed === "number" ? parsed.completed : null, total: typeof parsed.total === "number" ? parsed.total : null });
    if (eventName === "completed") onEvent({ type: "completed", response: parsed as unknown as SendChatMessageResponse });
    if (eventName === "failed") onEvent({ type: "failed", code: String(parsed.code), message: String(parsed.message) });
  };

  while (true) {
    const { value, done } = await reader.read();
    buffer += decoder.decode(value, { stream: !done });
    const frames = buffer.split(/\r?\n\r?\n/);
    buffer = frames.pop() ?? "";
    frames.forEach(dispatch);
    if (done) break;
  }
}