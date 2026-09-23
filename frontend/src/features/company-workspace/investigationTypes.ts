import type { Investigation, InvestigationOrganization } from "../../api/investigations";

export type WorkspaceInvestigation = Investigation & { organization?: InvestigationOrganization | null };

export const investigationPurposeLabel = (purpose: Investigation["purpose"]) =>
  purpose === "ProfileImprovement" ? "Profile improvement" : "General research";

export const investigationStatusLabel = (status: Investigation["status"]) =>
  status === "Ready" ? "Ready for review" : status;
