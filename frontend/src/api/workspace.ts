import { request } from "./client";
import type { Company } from "../types/company";

export type CompanyHealthStatus =
  | "Complete"
  | "Partial"
  | "Sparse"
  | "Unresearched"
  | "Stale"
  | "PendingUpdate"
  | "PossibleDuplicate"
  | "Archived";

export interface CompanyHealthAssessment {
  companyId: string;
  status: CompanyHealthStatus;
  baseStatus: CompanyHealthStatus;
  flags: CompanyHealthStatus[];
  hasAcceptedProfile: boolean;
  coveredTargetCount: number;
  requiredTargetCount: number;
  sourceDocumentCount: number;
  researchRunCount: number;
  lastResearchedAt?: string | null;
  missingTargets: string[];
  reasons: string[];
}

export interface WorkspaceReviewCompany {
  companyId: string;
  name: string;
  health: CompanyHealthAssessment;
}

export interface CompanyWorkspaceIdentity {
  companyId: string;
  name: string;
  legalName?: string | null;
  website?: string | null;
  country?: string | null;
  registrationNumber?: string | null;
}

export type DuplicateMatchType = "NameAndCountry" | "LegalNameAndCountry" | "WebsiteHost" | "RegistrationNumber";

export interface CompanyDuplicateGroup {
  groupId: string;
  strongestMatch: DuplicateMatchType;
  matchTypes: DuplicateMatchType[];
  members: CompanyWorkspaceIdentity[];
  rationale: string;
}

export type WorkspaceReviewRecommendationKind =
  | "PossibleDuplicate"
  | "PossibleAlias"
  | "SparseProfile"
  | "Unresearched"
  | "Stale"
  | "PendingUpdate";

export interface WorkspaceReviewRecommendation {
  kind: WorkspaceReviewRecommendationKind;
  companyId: string;
  title: string;
  summary: string;
  relatedCompanyIds: string[];
  reasons: string[];
  healthStatus?: CompanyHealthStatus | null;
  isAiGenerated: boolean;
}

export interface WorkspaceResearchReviewItem {
  itemId: string;
  companyId: string;
  companyName: string;
  method: "RAVEN Research" | "Deep Research" | "External AI Assist" | string;
  title: string;
  state: "Ready" | "Issue" | string;
  updatedAt: string;
  detail?: string | null;
  investigationId?: string | null;
  reviewKey?: string | null;
  occurrenceCount?: number;
  groupingNote?: string | null;
}

export interface WorkspaceResearchCleanupCandidate {
  reviewKey: string;
  companyId: string;
  companyName: string;
  method: string;
  title: string;
  reason: string;
  occurrenceCount: number;
  acknowledgedThrough: string;
}

export interface WorkspaceReviewResponse {
  companies: WorkspaceReviewCompany[];
  duplicateGroups: CompanyDuplicateGroup[];
  recommendations: WorkspaceReviewRecommendation[];
  aiUsed: boolean;
  aiWarning?: string | null;
  researchReady?: WorkspaceResearchReviewItem[];
  researchIssues?: WorkspaceResearchReviewItem[];
  researchCleanupCandidates?: WorkspaceResearchCleanupCandidate[];
}

export function buildWorkspaceResearchReviewKey(method: string, companyId: string, topic: string) {
  const normalizedTopic = topic.trim().split(/\s+/).join(" ").toLowerCase();
  return `${method}:${companyId}:${normalizedTopic.slice(0, 480)}`;
}

export interface CompanyMergePreview {
  canonicalCompanyId: string;
  duplicateCompanyId: string;
  canonicalCompanyName: string;
  duplicateCompanyName: string;
  researchRuns: number;
  researchCandidates: number;
  researchEvents: number;
  identityCandidates: number;
  sourceDocuments: number;
  duplicateSourceDocumentsToReuse: number;
  duplicateSourceDocumentsToMove: number;
  profileCandidates: number;
  profileVersions: number;
  profileEvidenceRows: number;
  profileChanges: number;
  deepResearchRuns: number;
  deepResearchActivities: number;
  savedInvestigations: number;
  hasCanonicalMonitoring: boolean;
  hasDuplicateMonitoring: boolean;
  warnings: string[];
}

export interface CompanyMergeResponse {
  canonicalCompany: Company;
  preview: CompanyMergePreview;
}

export function getWorkspaceReview(includeArchived = false) {
  return request<WorkspaceReviewResponse>("/api/companies/workspace-review", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ includeArchived, includeAiSuggestions: true }),
  });
}

export function acknowledgeWorkspaceResearch(items: Array<{ reviewKey: string; acknowledgedThrough: string }>) {
  return request<{ acknowledgedCount: number; hiddenOccurrenceCount: number; skippedCount: number }>("/api/companies/workspace-review/acknowledge", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ items }),
  });
}

export function cleanupWorkspaceResearch(includeArchived = false) {
  return request<{ acknowledgedCount: number; hiddenOccurrenceCount: number; skippedCount: number }>("/api/companies/workspace-review/cleanup", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ includeArchived }),
  });
}

export function archiveCompany(id: string) {
  return request<Company>(`/api/companies/${encodeURIComponent(id)}/archive`, { method: "POST" });
}

export function restoreCompany(id: string) {
  return request<Company>(`/api/companies/${encodeURIComponent(id)}/restore`, { method: "POST" });
}

export function deleteCompany(id: string) {
  return request<void>(`/api/companies/${encodeURIComponent(id)}`, {
    method: "DELETE",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ confirm: true }),
  });
}

export function previewCompanyMerge(canonicalCompanyId: string, duplicateCompanyId: string) {
  return request<CompanyMergePreview>("/api/companies/merge/preview", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ canonicalCompanyId, duplicateCompanyId }),
  });
}

export function confirmCompanyMerge(canonicalCompanyId: string, duplicateCompanyId: string) {
  return request<CompanyMergeResponse>("/api/companies/merge/confirm", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ canonicalCompanyId, duplicateCompanyId, confirm: true }),
  });
}
