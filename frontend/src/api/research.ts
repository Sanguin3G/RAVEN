import { request } from "./client";
import type {
  AcquireResearchCandidatesRequest,
  GroundingMode,
  ResearchCandidate,
  ResearchIdentityCandidate,
  ResearchRun,
  SourceDocument,
} from "../types/research";

export type { ResearchRun, SourceDocument } from "../types/research";

export function discoverResearch(
  companyId: string,
  researchHint?: string,
  groundingMode?: GroundingMode,
  useAcceptedProfileIdentity = false,
) {
  const body: { researchHint?: string; groundingMode?: GroundingMode; useAcceptedProfileIdentity?: boolean } = {};
  if (researchHint?.trim()) body.researchHint = researchHint.trim();
  if (groundingMode) body.groundingMode = groundingMode;
  if (useAcceptedProfileIdentity) body.useAcceptedProfileIdentity = true;

  return request<ResearchRun>(`/api/companies/${encodeURIComponent(companyId)}/research/discover`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
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
