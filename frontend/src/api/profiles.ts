import { request } from "./client";
import type { CompanyProfileVersion, ProfileGenerationResponse } from "../types/profile";

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
