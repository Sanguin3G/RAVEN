import type { SourceDocument } from "./research";

export interface ProfileEvidence {
  id: string;
  fieldPath: string;
  sourceDocumentIds: string[];
}

export interface CompanyProfileCandidate {
  id: string;
  companyId: string;
  researchRunId: string;
  generatedAt: string;
  aiProvider?: string | null;
  aiModel?: string | null;
  displayName?: string | null;
  legalName?: string | null;
  website?: string | null;
  country?: string | null;
  headquarters?: string | null;
  registrationNumberOrTaxId?: string | null;
  foundedYear?: number | null;
  primaryIndustry?: string | null;
  secondaryIndustries: string[];
  companySize?: string | null;
  employeeCount?: number | null;
  employeeCountRange?: string | null;
  summary?: string | null;
  productsServices: Array<{ name: string; type?: string | null; description?: string | null }>;
  markets: Array<{ name: string; type?: string | null }>;
  leadership: Array<{ name: string; title?: string | null }>;
  locations: Array<{ name?: string | null; address?: string | null; country?: string | null; type?: string | null }>;
  publicLinks: Array<{ url: string; kind?: string | null; label?: string | null }>;
  evidence: ProfileEvidence[];
  validationWarnings: string[];
}

export interface CompanyProfileVersion extends CompanyProfileCandidate {
  version: number;
  confirmedAt: string;
}

export interface ProfileGenerationResponse {
  candidate?: CompanyProfileCandidate | null;
  warnings: string[];
  provider: string;
  model: string;
  promptTemplateVersion: string;
  durationMs: number;
  failure?: { code: string; message: string } | null;
}

export type ProfileEvidenceSource = Pick<SourceDocument, "id" | "url" | "title" | "sourceDomain" | "sourceKind" | "iconUrl" | "contentPreview" | "retrievedAt" | "crawlerProvider">;
