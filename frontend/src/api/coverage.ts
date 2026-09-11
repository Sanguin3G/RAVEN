import { request } from "./client";

export type ResearchTarget =
  | "LegalIdentity"
  | "TaxRegistration"
  | "FoundedHistory"
  | "Industry"
  | "EmployeeScale"
  | "ProductsServices"
  | "Markets"
  | "Leadership"
  | "Locations";

export type CoverageLevel = "Missing" | "Weak" | "Supported" | "Strong";

export interface EvidenceCoverageItem {
  target: ResearchTarget;
  level: CoverageLevel;
  supportingSourceCount: number;
  strongestSourceKind?: string | null;
  reasons: string[];
}

export interface EvidenceCoverageResponse {
  companyId: string;
  researchRunId?: string | null;
  items: EvidenceCoverageItem[];
  budgetExhausted: boolean;
}

export function getCompanyCoverage(companyId: string) {
  return request<EvidenceCoverageResponse>(`/api/companies/${encodeURIComponent(companyId)}/coverage`);
}

export function getResearchRunCoverage(researchRunId: string) {
  return request<EvidenceCoverageResponse>(`/api/research-runs/${encodeURIComponent(researchRunId)}/coverage`);
}
