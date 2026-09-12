import { request } from "./client";
import type {
  AcquireResearchCandidatesRequest,
  GroundingMode,
  ResearchCandidate,
  ResearchIdentityCandidate,
  ResearchMode,
  ResearchTarget,
  ResearchRun,
  ActiveResearchRun,
  ResearchExecution,
  SourceDocument,
} from "../types/research";

export type { ResearchRun, SourceDocument } from "../types/research";

export type { ActiveResearchRun } from "../types/research";

export interface DiscoverResearchOptions {
  mode?: ResearchMode;
  targets?: ResearchTarget[];
  baseProfileVersionId?: string;
}

export function discoverResearch(
  companyId: string,
  researchHint?: string,
  groundingMode?: GroundingMode,
  useAcceptedProfileIdentity = false,
  options?: DiscoverResearchOptions,
) {
  const body: {
    researchHint?: string;
    groundingMode?: GroundingMode;
    useAcceptedProfileIdentity?: boolean;
    mode?: ResearchMode;
    targets?: ResearchTarget[];
    baseProfileVersionId?: string;
  } = {};
  if (researchHint?.trim()) body.researchHint = researchHint.trim();
  if (groundingMode) body.groundingMode = groundingMode;
  if (useAcceptedProfileIdentity) body.useAcceptedProfileIdentity = true;
  if (options?.mode) body.mode = options.mode;
  if (options?.targets?.length) body.targets = options.targets;
  if (options?.baseProfileVersionId) body.baseProfileVersionId = options.baseProfileVersionId;

  return request<ResearchRun>(`/api/companies/${encodeURIComponent(companyId)}/research/discover`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}

export function startBackgroundResearch(
  companyId: string,
  researchHint?: string,
  groundingMode?: GroundingMode,
  useAcceptedProfileIdentity = false,
  options?: DiscoverResearchOptions,
) {
  const body: Record<string, unknown> = {};
  if (researchHint?.trim()) body.researchHint = researchHint.trim();
  if (groundingMode) body.groundingMode = groundingMode;
  if (useAcceptedProfileIdentity) body.useAcceptedProfileIdentity = true;
  if (options?.mode) body.mode = options.mode;
  if (options?.targets?.length) body.targets = options.targets;
  if (options?.baseProfileVersionId) body.baseProfileVersionId = options.baseProfileVersionId;
  return request<ResearchRun>(`/api/companies/${encodeURIComponent(companyId)}/research/start`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}

export function getActiveResearchRuns() {
  return request<ActiveResearchRun[]>("/api/research-runs/active");
}

export function cancelResearchRun(researchRunId: string) {
  return request<ResearchRun>(`/api/research-runs/${encodeURIComponent(researchRunId)}/cancel`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
  });
}

export function getResearchIdentityCandidates(researchRunId: string) {
  return request<ResearchIdentityCandidate[]>(`/api/research-runs/${encodeURIComponent(researchRunId)}/identity-candidates`);
}

export function selectResearchIdentityCandidate(researchRunId: string, candidateId: string) {
  return request<ResearchRun>(`/api/research-runs/${encodeURIComponent(researchRunId)}/identity/select`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ candidateId }),
  });
}

export function getResearchRun(researchRunId: string) {
  return request<ResearchRun>(`/api/research-runs/${encodeURIComponent(researchRunId)}`);
}

export function getResearchExecution(researchRunId: string) {
  return request<ResearchExecution>(`/api/research-runs/${encodeURIComponent(researchRunId)}/execution`);
}

export function getResearchCandidates(researchRunId: string) {
  return request<ResearchCandidate[]>(`/api/research-runs/${encodeURIComponent(researchRunId)}/candidates`);
}

export function acquireResearchCandidates(researchRunId: string, candidateIds: string[]) {
  const body: AcquireResearchCandidatesRequest = { candidateIds };
  return request<ResearchRun>(`/api/research-runs/${encodeURIComponent(researchRunId)}/acquire`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}

export function getResearchSources(researchRunId: string) {
  return request<SourceDocument[]>(`/api/research-runs/${encodeURIComponent(researchRunId)}/sources`);
}

/** Compatibility endpoint retained for existing callers while the UI uses staged research. */
export function startResearch(companyId: string) {
  return request<ResearchRun>(`/api/companies/${encodeURIComponent(companyId)}/research`, { method: "POST" });
}

export function getCompanySources(companyId: string) {
  return request<SourceDocument[]>(`/api/companies/${encodeURIComponent(companyId)}/sources`);
}
