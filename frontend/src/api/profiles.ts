import { request } from "./client";
import type { CompanyProfileCandidate, CompanyProfileVersion, ProfileGenerationResponse } from "../types/profile";
import type { ResearchTarget } from "./coverage";
import type { ResearchRun } from "../types/research";

export interface StartTargetedResearchRequest {
  targets: ResearchTarget[];
  baseProfileVersionId?: string | null;
}

export interface ProfilePatchChange {
  fieldPath: string;
  oldValue?: string | null;
  proposedValue?: string | null;
  evidenceSourceDocumentIds: string[];
}

/** A server-owned patch candidate. The client may review/confirm it, but never author its values. */
export interface ProfilePatchCandidate {
  candidateId: string;
  researchRunId: string;
  baseProfileVersionId: string;
  allowedTargets: ResearchTarget[];
  changes: ProfilePatchChange[];
  warnings: string[];
}

export function generateCompanyProfile(researchRunId: string) {
  return request<ProfileGenerationResponse>(`/api/research-runs/${encodeURIComponent(researchRunId)}/profile/generate`, { method: "POST" });
}

export function confirmCompanyProfile(researchRunId: string, candidateId: string) {
  return request<CompanyProfileVersion>(`/api/research-runs/${encodeURIComponent(researchRunId)}/profile/confirm`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ candidateId }),
  });
}

export function getCurrentCompanyProfile(companyId: string) {
  return request<CompanyProfileVersion>(`/api/companies/${encodeURIComponent(companyId)}/profile`);
}

export function getCompanyProfileCandidate(researchRunId: string) {
  return request<CompanyProfileCandidate>(`/api/research-runs/${encodeURIComponent(researchRunId)}/profile/candidate`);
}

export function startTargetedResearch(companyId: string, requestBody: StartTargetedResearchRequest) {
  return request<ResearchRun>(`/api/companies/${encodeURIComponent(companyId)}/research/targeted`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(requestBody),
  });
}

export function generateProfilePatch(researchRunId: string) {
  return request<ProfilePatchCandidate>(`/api/research-runs/${encodeURIComponent(researchRunId)}/profile-patch/generate`, {
    method: "POST",
  });
}

export function confirmProfilePatch(researchRunId: string, candidateId: string) {
  return request<CompanyProfileVersion>(`/api/research-runs/${encodeURIComponent(researchRunId)}/profile-patch/confirm`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ candidateId }),
  });
}
