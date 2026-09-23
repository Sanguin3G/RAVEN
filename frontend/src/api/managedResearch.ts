import { request } from "./client";

export type ManagedResearchJobStatus = "Queued" | "Researching" | "Completed" | "Failed" | "Cancelled";
export type ManagedResearchEffort = "Auto" | "Low" | "Medium" | "High" | "XHigh";
export type ManagedResearchPurpose = "General" | "ProfileImprovement";

export interface ManagedResearchBriefPreview {
  question: string;
  contextRevision: string;
}

export interface ManagedResearchJob {
  id: string;
  companyId: string;
  conversationId?: string | null;
  chatMessageId?: string | null;
  objective: string;
  purpose?: ManagedResearchPurpose;
  provider?: string | null;
  status: ManagedResearchJobStatus;
  providerRunId?: string | null;
  createdAt: string;
  startedAt?: string | null;
  completedAt?: string | null;
  result?: unknown;
  providerCostDollars?: number | null;
  investigationId?: string | null;
  error?: string | null;
}

export interface ResearchContextAttachment {
  id: string;
  companyId: string;
  conversationId: string;
  investigationId: string;
  origin: string;
  objective: string;
  summary: string;
  completedAt: string;
  attachedAt: string;
}

export interface StartManagedResearchOptions {
  conversationId?: string;
  chatMessageId?: string;
  effort?: ManagedResearchEffort;
  purpose?: ManagedResearchPurpose;
  contextRevision?: string;
}

function companyPath(companyId: string) {
  return `/api/companies/${encodeURIComponent(companyId)}/managed-research`;
}

export function startManagedResearch(companyId: string, objective: string, options?: StartManagedResearchOptions) {
  const body: Record<string, unknown> = { objective: objective.trim() };
  if (options?.conversationId) body.conversationId = options.conversationId;
  if (options?.chatMessageId) body.chatMessageId = options.chatMessageId;
  if (options?.effort) body.effort = options.effort;
  if (options?.purpose) body.purpose = options.purpose;
  if (options?.contextRevision) body.contextRevision = options.contextRevision;

  return request<ManagedResearchJob>(companyPath(companyId), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}

export function previewManagedResearchBrief(
  companyId: string,
  question: string) {
  return request<ManagedResearchBriefPreview>(`${companyPath(companyId)}/preview`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ question: question.trim() }),
  });
}

export function getManagedResearchJobs(companyId: string) {
  return request<ManagedResearchJob[]>(companyPath(companyId));
}

function contextAttachmentPath(companyId: string, investigationId?: string) {
  const base = investigationId
    ? `${companyPath(companyId)}/${encodeURIComponent(investigationId)}/context-attachments`
    : `/api/companies/${encodeURIComponent(companyId)}/research-context-attachments`;
  return base;
}

export function getResearchContextAttachments(companyId: string, conversationId: string) {
  return request<ResearchContextAttachment[]>(
    `${contextAttachmentPath(companyId)}?conversationId=${encodeURIComponent(conversationId)}`);
}

export function attachResearchContext(
  companyId: string,
  investigationId: string,
  conversationId: string) {
  return request<ResearchContextAttachment>(contextAttachmentPath(companyId, investigationId), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ conversationId }),
  });
}

export function removeResearchContext(
  companyId: string,
  investigationId: string,
  conversationId: string) {
  return request<void>(
    `${contextAttachmentPath(companyId, investigationId)}?conversationId=${encodeURIComponent(conversationId)}`,
    { method: "DELETE" });
}
