import { request } from "./client";

export type SavedResearchType = "Fast" | "Deep";

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
}

export function getSavedInvestigations(companyId: string) {
  return request<SavedResearchArtifact[]>(`/api/companies/${encodeURIComponent(companyId)}/saved-research`);
}

export function getSavedInvestigation(companyId: string, artifactId: string) {
  return request<SavedResearchArtifact>(`/api/companies/${encodeURIComponent(companyId)}/saved-research/${encodeURIComponent(artifactId)}`);
}
