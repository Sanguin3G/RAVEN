import { request } from "./client";
import type { ResearchTarget } from "./coverage";
import type { SavedResearchArtifact } from "./investigations";

export interface ExternalResearchBrief {
  objective: string;
  focusedTargets: ResearchTarget[];
  markdown: string;
}

export interface GenerateExternalResearchBriefRequest {
  researchObjective?: string;
  requestedTargets?: ResearchTarget[];
}

export interface ImportExternalResearchRequest {
  question: string;
  markdown: string;
  title?: string;
  conversationId?: string;
}

export interface ExternalResearchImportPreview {
  summary: string;
  claims: Array<{
    field: string;
    statement: string;
    supportingSourceLeadIds?: string[] | null;
    confidence?: string | null;
    notes?: string | null;
  }>;
  sourceLeads: Array<{
    id: string;
    url: string;
    title?: string | null;
    publisher?: string | null;
    supports?: string | null;
  }>;
  uncertainties: string[];
  suggestedFollowUps: string[];
  rawMarkdown: string;
}

export type ExternalResearchAnalysisStatus = "Queued" | "Analyzing" | "Completed" | "Failed";

export interface ExternalResearchAnalysisJob {
  id: string;
  companyId: string;
  question: string;
  status: ExternalResearchAnalysisStatus;
  result?: ExternalResearchImportPreview | null;
  createdAt: string;
  completedAt?: string | null;
  error?: string | null;
}

export function generateExternalResearchBrief(companyId: string, body: GenerateExternalResearchBriefRequest) {
  return request<ExternalResearchBrief>(`/api/companies/${encodeURIComponent(companyId)}/external-research/brief`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}

export function importExternalResearch(companyId: string, body: ImportExternalResearchRequest) {
  return request<SavedResearchArtifact>(`/api/companies/${encodeURIComponent(companyId)}/external-research/import`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}

export function previewExternalResearchImport(companyId: string, body: ImportExternalResearchRequest) {
  return request<ExternalResearchImportPreview>(`/api/companies/${encodeURIComponent(companyId)}/external-research/import/preview`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}

export function startExternalResearchAnalysis(companyId: string, body: ImportExternalResearchRequest) {
  return request<ExternalResearchAnalysisJob>(`/api/companies/${encodeURIComponent(companyId)}/external-research/analyze`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}

export function getExternalResearchAnalysis(companyId: string, jobId: string) {
  return request<ExternalResearchAnalysisJob>(`/api/companies/${encodeURIComponent(companyId)}/external-research/analyze/${encodeURIComponent(jobId)}`);
}
