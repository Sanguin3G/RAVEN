import type { CandidateSource, EvidenceRecord } from "../../components/sources";
import type { CompanyMatchResponse } from "../../types/company";
import type { ResearchCandidate, ResearchIdentityCandidate, ResearchTarget, SourceDocument } from "../../types/research";

export function entityTypeLabel(entityType: ResearchIdentityCandidate["entityType"]) {
  switch (entityType) {
    case "ParentGroup":
      return "Parent group";
    case "Subsidiary":
      return "Subsidiary";
    case "Affiliate":
      return "Affiliate";
    case "Brand":
      return "Brand";
    case "Company":
      return "Company";
    default:
      return "Organization";
  }
}

export function confidenceLabel(confidence: ResearchIdentityCandidate["confidence"]) {
  return `${confidence.toLowerCase()} confidence`;
}

export function targetLabel(target: ResearchTarget) {
  return target.replace(/([a-z])([A-Z])/g, "$1 $2").replace("Products Services", "Products / services");
}

export function relationshipLabel(relationship?: ResearchCandidate["entityRelationship"]) {
  switch (relationship) {
    case "SameEntity":
      return "Matches selected entity";
    case "Parent":
      return "Related parent group";
    case "Subsidiary":
      return "Related subsidiary";
    case "Affiliate":
      return "Related affiliate";
    case "DifferentEntity":
      return "Likely different entity";
    case "Uncertain":
      return "Entity relationship uncertain";
    default:
      return undefined;
  }
}

export function semanticSummary(candidate: ResearchCandidate) {
  const relationship = relationshipLabel(candidate.entityRelationship);
  const relevance = candidate.semanticRelevance ? `${candidate.semanticRelevance.toLowerCase()} relevance` : undefined;
  const purposes = candidate.semanticPurposes?.slice(0, 3).join(", ");
  const rationale = candidate.semanticRationale?.trim();
  return [relationship, relevance, purposes ? `Useful for ${purposes}` : undefined, rationale].filter(Boolean).join(" · ");
}

export function matchStrengthLabel(strength: CompanyMatchResponse["matchStrength"]) {
  switch (strength) {
    case "Exact":
      return "Exact identity match";
    case "VeryStrong":
      return "Very strong match";
    case "Strong":
      return "Strong match";
    default:
      return "Possible match";
  }
}

export function formatDate(value?: string | null) {
  if (!value) return "Not researched yet";
  const timestamp = Date.parse(value);
  if (Number.isNaN(timestamp)) return "Not researched yet";
  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(timestamp);
}

function candidateStatus(candidate: ResearchCandidate): CandidateSource["acquisitionStatus"] {
  switch (candidate.acquisitionStatus) {
    case "Acquiring":
      return "pending";
    case "Acquired":
      return "acquired";
    case "Failed":
    case "Unavailable":
      return "failed";
    case "DuplicateSkipped":
      return "duplicate";
    default:
      return "idle";
  }
}

export function toCandidateSource(candidate: ResearchCandidate): CandidateSource {
  const aiSummary = semanticSummary(candidate);
  const displaySnippet = aiSummary
    ? [candidate.snippet, `RAVEN assessment: ${aiSummary}`].filter(Boolean).join(" · ")
    : candidate.snippet;
  const recommendationReasons = aiSummary && candidate.recommended
    ? [...candidate.recommendationReasons, `AI: ${aiSummary}`]
    : candidate.recommendationReasons;

  return {
    id: candidate.id,
    url: candidate.url,
    title: candidate.title || candidate.domain || "Untitled source",
    domain: candidate.domain,
    snippet: displaySnippet,
    kind: candidate.sourceKind,
    iconUrl: candidate.iconUrl,
    recommended: candidate.recommended,
    recommendationReasons,
    selected: candidate.selected,
    acquisitionStatus: candidateStatus(candidate),
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
