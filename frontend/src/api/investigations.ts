import { request } from "./client";

export type SavedResearchType = "Fast" | "Deep";
export type SavedResearchOrigin = "RavenNative" | "ManagedAi" | "ExternalImport";

export interface ResearchSourceLead {
  id: string;
  url: string;
  title?: string | null;
  publisher?: string | null;
  publishedAt?: string | null;
  sourceType?: string | null;
  supports?: string | null;
}

export interface ResearchClaim {
  field: string;
  statement: string;
  supportingSourceLeadIds?: string[] | null;
  confidence?: string | null;
  notes?: string | null;
}

export interface SavedResearchArtifact {
  id: string;
  companyId: string;
  conversationId?: string | null;
  deepResearchRunId?: string | null;
  title: string;
  question: string;
  summary: string;
  result?: string;
  createdAt: string;
  researchType: SavedResearchType;
  model?: string | null;
  sourceCount: number;
  sourceDocumentIds: string[];
  origin?: SavedResearchOrigin;
  provider?: string | null;
  objective?: string | null;
  completedAt?: string | null;
  managedResearchJobId?: string | null;
  providerMetadata?: Record<string, string>;
  sourceLeads?: ResearchSourceLead[];
  claims?: ResearchClaim[];
  uncertainties?: string[];
}

export function getSavedInvestigations(companyId: string) {
  return request<SavedResearchArtifact[]>(`/api/companies/${encodeURIComponent(companyId)}/saved-research`);
}

export function getSavedInvestigation(companyId: string, artifactId: string) {
  return request<SavedResearchArtifact>(`/api/companies/${encodeURIComponent(companyId)}/saved-research/${encodeURIComponent(artifactId)}`);
}
