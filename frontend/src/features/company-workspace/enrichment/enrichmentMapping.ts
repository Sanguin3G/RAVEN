import type { ResearchTarget } from "../../../api/coverage";
import type { ResearchCandidate, SourceDocument } from "../../../types/research";
import type { CandidateSource, EvidenceRecord } from "../../../components/sources";

export const targetOrder: ResearchTarget[] = [
  "LegalIdentity",
  "TaxRegistration",
  "FoundedHistory",
  "Industry",
  "EmployeeScale",
  "ProductsServices",
  "Markets",
  "Leadership",
  "Locations",
];

export const targetLabels: Record<ResearchTarget, string> = {
  LegalIdentity: "Legal identity",
  TaxRegistration: "Tax registration",
  FoundedHistory: "Founded / history",
  Industry: "Industry",
  EmployeeScale: "Company scale",
  ProductsServices: "Products & services",
  Markets: "Markets & customer segments",
  Leadership: "Leadership",
  Locations: "Locations",
};

export function toCandidateSource(candidate: ResearchCandidate): CandidateSource {
  const semantic = [
    candidate.entityRelationship ? `Entity: ${candidate.entityRelationship}` : undefined,
    candidate.semanticRelevance ? `${candidate.semanticRelevance} relevance` : undefined,
    candidate.semanticPurposes?.length ? `Covers ${candidate.semanticPurposes.slice(0, 3).join(" · ")}` : undefined,
    candidate.semanticRationale,
  ].filter(Boolean).join(" · ");
  return {
    id: candidate.id,
    url: candidate.url,
    title: candidate.title || candidate.domain || "Untitled source",
    domain: candidate.domain,
    snippet: [candidate.snippet, semantic].filter(Boolean).join(" · "),
    kind: candidate.sourceKind,
    iconUrl: candidate.iconUrl,
    recommended: candidate.recommended,
    recommendationReasons: candidate.recommendationReasons,
    selected: candidate.selected || candidate.recommended,
    acquisitionStatus: candidate.acquisitionStatus === "Acquired" ? "acquired" : candidate.acquisitionStatus === "Failed" || candidate.acquisitionStatus === "Unavailable" ? "failed" : candidate.acquisitionStatus === "DuplicateSkipped" ? "duplicate" : candidate.acquisitionStatus === "Acquiring" ? "pending" : "idle",
    acquisitionMessage: candidate.acquisitionError,
  };
}

export function toEvidenceRecord(source: SourceDocument): EvidenceRecord {
  return {
    id: source.id,
    url: source.url,
    title: source.title || source.sourceDomain || "Untitled source",
    domain: source.sourceDomain,
    kind: source.sourceKind,
    iconUrl: source.iconUrl,
    preview: source.contentPreview,
    retrievedAt: source.retrievedAt,
    crawlerProvider: source.crawlerProvider,
    status: "acquired",
  };
}

export function fieldLabel(path: string) {
  const labels: Record<string, string> = {
    displayName: "Display name",
    legalName: "Legal name",
    website: "Official website",
    country: "Country",
    headquarters: "Headquarters",
    registrationNumberOrTaxId: "Registration / tax ID",
    foundedYear: "Founded year",
    primaryIndustry: "Primary industry",
    secondaryIndustries: "Secondary industries",
    companySize: "Company scale",
    employeeCount: "Employee count",
    employeeCountRange: "Employee range",
    productsServices: "Products & services",
    markets: "Markets",
    leadership: "Leadership",
    locations: "Locations",
  };
  return labels[path] || path;
}

function formatStructuredValue(value: unknown): string {
  if (value === null || value === undefined || value === "") return "";
  if (typeof value === "string" || typeof value === "number" || typeof value === "boolean") return String(value);
  if (Array.isArray(value)) return value.map(formatStructuredValue).filter(Boolean).join(", ");
  if (typeof value === "object") {
    const record = value as Record<string, unknown>;
    const preferred = record.name ?? record.fullName ?? record.title ?? record.role ?? record.value;
    if (preferred !== undefined) return formatStructuredValue(preferred);
    return Object.entries(record).map(([key, item]) => {
      const formatted = formatStructuredValue(item);
      return formatted ? key + ": " + formatted : "";
    }).filter(Boolean).join("; ");
  }
  return "";
}

export function prettyValue(value?: string | null) {
  if (!value) return "Not verified";
  try {
    const parsed = JSON.parse(value) as unknown;
    const formatted = formatStructuredValue(parsed);
    return formatted || "Not verified";
  } catch {
    return value;
  }
}
