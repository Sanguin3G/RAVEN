export interface Company {
  id: string;
  name: string;
  legalName?: string | null;
  registrationNumber?: string | null;
  headquarters?: string | null;
  website?: string | null;
  country?: string | null;
  createdAt: string;
  updatedAt: string;
  lastResearchedAt?: string | null;
  archivedAt?: string | null;
}

export interface CreateCompanyRequest {
  name: string;
  website?: string;
  country?: string;
  legalName?: string;
  registrationNumber?: string;
  headquarters?: string;
}

export interface CompanyMatchRequest {
  name?: string;
  website?: string;
  country?: string;
  legalName?: string;
  registrationNumber?: string;
}

export type CompanyMatchStrength = "Exact" | "VeryStrong" | "Strong" | "Weak";

export interface CompanyMatchResponse {
  company: Company;
  matchStrength: CompanyMatchStrength;
  matchReason: string;
}
