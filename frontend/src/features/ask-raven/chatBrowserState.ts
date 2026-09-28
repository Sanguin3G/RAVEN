const activeKey = "raven:active-chat-by-company";

function readPointers(): Record<string, string> {
  try {
    const value = JSON.parse(localStorage.getItem(activeKey) ?? "{}");
    return value && typeof value === "object" && !Array.isArray(value) ? value : {};
  } catch {
    return {};
  }
}

export function getActiveChat(companyId: string): string | null {
  const value = readPointers()[companyId];
  return typeof value === "string" ? value : null;
}

export function setActiveChat(companyId: string, conversationId: string | null) {
  try {
    const pointers = readPointers();
    if (conversationId) pointers[companyId] = conversationId;
    else delete pointers[companyId];
    localStorage.setItem(activeKey, JSON.stringify(pointers));
  } catch { /* Chat remains server-persisted when browser storage is unavailable. */ }
}

export function chatDraftKey(companyId: string, conversationId: string | null) {
  return `raven:chat-draft:${companyId}:${conversationId ?? "new"}`;
}
