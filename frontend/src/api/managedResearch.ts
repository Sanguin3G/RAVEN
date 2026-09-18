import { request } from "./client";

export type ManagedResearchJobStatus = "Queued" | "Researching" | "Completed" | "Failed" | "Cancelled";
export type ManagedResearchEffort = "Auto" | "Low" | "Medium" | "High" | "XHigh";

export interface ManagedResearchJob {
  id: string;
  companyId: string;
  conversationId?: string | null;
  chatMessageId?: string | null;
  objective: string;
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

export interface StartManagedResearchOptions {
  conversationId?: string;
  chatMessageId?: string;
  effort?: ManagedResearchEffort;
}

function companyPath(companyId: string) {
  return `/api/companies/${encodeURIComponent(companyId)}/managed-research`;
}

export function startManagedResearch(companyId: string, objective: string, options?: StartManagedResearchOptions) {
  const body: Record<string, string> = { objective: objective.trim() };
  if (options?.conversationId) body.conversationId = options.conversationId;
  if (options?.chatMessageId) body.chatMessageId = options.chatMessageId;
  if (options?.effort) body.effort = options.effort;

  return request<ManagedResearchJob>(companyPath(companyId), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}

export function getManagedResearchJobs(companyId: string) {
  return request<ManagedResearchJob[]>(companyPath(companyId));
}
