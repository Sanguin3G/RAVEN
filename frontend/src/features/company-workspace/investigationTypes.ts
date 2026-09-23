import type { ResearchClaim, ResearchSourceLead, SavedResearchArtifact, InvestigationOrganization } from "../../api/investigations";

export interface WorkspaceInvestigation {
  id: string;
  artifactId?: string;
  materialId?: string;
  materialKind?: "saved" | "managed";
  title: string;
  objective: string;
  summary: string;
  origin: "RAVEN Research" | "Deep Research" | "External AI Assist";
  category: InvestigationCategory;
  status: "Running" | "Ready for review" | "Failed";
  updatedAt: string;
  provider?: string | null;
  claims: ResearchClaim[];
  sourceLeads: ResearchSourceLead[];
  uncertainties: string[];
  rawMaterial?: string | null;
  rawResponse?: string | null;
  organization?: InvestigationOrganization | null;
  locked?: boolean;
}

export type InvestigationCategory = "Profile improvement" | "Financial / performance" | "Market / strategy" | "General research";

const profileTerms = ["leadership", "leader", "executive", "employee", "headcount", "workforce", "market", "customer", "location", "office", "headquarter", "product", "service", "industry", "founded", "history", "legal", "identity", "registration", "tax", "lãnh đạo", "nhân sự", "sản phẩm", "dịch vụ", "khách hàng", "địa điểm", "ngành", "thành lập", "pháp lý", "đăng ký thuế"];
const financialTerms = ["revenue", "financial", "profit", "ebitda", "margin", "cash flow", "funding", "valuation", "earnings", "annual report", "income", "doanh thu", "lợi nhuận", "tài chính", "dòng tiền"];
const marketTerms = ["market", "expansion", "competitor", "competition", "partnership", "customer", "strategy", "geography", "country", "business model", "mô hình kinh doanh", "chiến lược", "cạnh tranh", "mở rộng thị trường"];

export function classifyInvestigation(text: string, claims: ResearchClaim[] = []): InvestigationCategory {
  const normalized = `${text} ${claims.map((claim) => `${claim.field} ${claim.statement}`).join(" ")}`.toLowerCase();
  if (financialTerms.some((term) => normalized.includes(term))) return "Financial / performance";
  if (profileTerms.some((term) => normalized.includes(term))) return "Profile improvement";
  if (marketTerms.some((term) => normalized.includes(term))) return "Market / strategy";
  return "General research";
}

export function artifactToWorkspace(artifact: SavedResearchArtifact): WorkspaceInvestigation {
  const origin = artifact.origin === "ManagedAi"
    ? "Deep Research"
    : artifact.origin === "ExternalImport"
      ? "External AI Assist"
      : "RAVEN Research";

  return {
    id: artifact.id,
    artifactId: artifact.id,
    materialId: artifact.id,
    materialKind: "saved",
    title: artifact.title,
    objective: artifact.objective || artifact.question,
    summary: artifact.summary,
    origin,
    category: classifyInvestigation(`${artifact.title} ${artifact.objective || artifact.question}`, artifact.claims || []),
    status: "Ready for review",
    updatedAt: artifact.completedAt || artifact.createdAt,
    provider: artifact.provider,
    claims: artifact.claims || [],
    sourceLeads: artifact.sourceLeads || [],
    uncertainties: artifact.uncertainties || [],
    rawMaterial: artifact.result,
    rawResponse: artifact.rawResponse,
    organization: artifact.organization,
  };
}
