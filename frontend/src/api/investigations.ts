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
  rawResponse?: string | null;
  organization?: InvestigationOrganization | null;
}

export interface InvestigationOrganizationTheme {
  name: string;
  summary: string;
  claimCount: number;
}

export interface InvestigationOrganization {
  id: string;
  savedResearchArtifactId: string;
  version: number;
  createdAt: string;
  executiveSummary: string;
  themes: InvestigationOrganizationTheme[];
  evidenceGaps: string[];
  suggestedFollowUps: string[];
  uncertainties: string[];
  isHumanEdited: boolean;
}

export type InvestigationPurpose = "GeneralResearch" | "ProfileImprovement";
export type InvestigationStatus = "Running" | "Ready" | "Done" | "Failed";

export interface Investigation {
  id: string;
  companyId: string;
  materialKind: "Saved" | "Managed";
  materialId?: string | null;
  title: string;
  objective: string;
  summary: string;
  origin: "RAVEN Research" | "Deep Research" | "External AI Assist";
  purpose: InvestigationPurpose;
  topics: string[];
  status: InvestigationStatus;
  materialUpdatedAt: string;
  doneThrough?: string | null;
  doneAt?: string | null;
  appliedProfileVersionId?: string | null;
  appliedAt?: string | null;
  profileImprovementLocked: boolean;
  provider?: string | null;
  claims: ResearchClaim[];
  sourceLeads: ResearchSourceLead[];
  uncertainties: string[];
  rawMaterial?: string | null;
  rawResponse?: string | null;
  briefingIds: string[];
}

export function getInvestigations(companyId: string) {
  return request<Investigation[]>(`/api/companies/${encodeURIComponent(companyId)}/investigations`);
}

export function markInvestigationDone(companyId: string, id: string) {
  return request<Investigation>(`/api/companies/${encodeURIComponent(companyId)}/investigations/${encodeURIComponent(id)}/done`, { method: "POST" });
}

export function reopenInvestigation(companyId: string, id: string) {
  return request<Investigation>(`/api/companies/${encodeURIComponent(companyId)}/investigations/${encodeURIComponent(id)}/reopen`, { method: "POST" });
}

export function getSavedInvestigations(companyId: string) {
  return request<SavedResearchArtifact[]>(`/api/companies/${encodeURIComponent(companyId)}/saved-research`);
}

export function getSavedInvestigation(companyId: string, artifactId: string) {
  return request<SavedResearchArtifact>(`/api/companies/${encodeURIComponent(companyId)}/saved-research/${encodeURIComponent(artifactId)}`);
}

export function getInvestigationOrganization(companyId: string, artifactId: string) {
  return request<InvestigationOrganization>(`/api/companies/${encodeURIComponent(companyId)}/saved-research/${encodeURIComponent(artifactId)}/organization`);
}

export function organizeInvestigation(companyId: string, artifactId: string) {
  return request<InvestigationOrganization>(`/api/companies/${encodeURIComponent(companyId)}/saved-research/${encodeURIComponent(artifactId)}/organization`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: "{}",
  });
}
