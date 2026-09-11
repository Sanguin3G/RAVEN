export type ResearchRunStatus = "Searching" | "Crawling" | "Completed" | "Failed";

export type GroundingMode = "Auto" | "Always" | "Off";

export type ResearchStage =
  | "Identifying"
  | "Grounding"
  | "AwaitingIdentitySelection"
  | "Discovering"
  | "AwaitingSourceSelection"
  | "Acquiring"
  | "EvidenceReady"
  | "GeneratingProfile"
  | "AwaitingProfileConfirmation"
  | "Completed"
  | "Failed";

export type SourceKind =
  | "OfficialWebsite"
  | "OfficialDocument"
  | "OfficialBusinessRegistry"
  | "BusinessDirectory"
  /** @deprecated Retained for documents created before Day 5 taxonomy. */
  | "BusinessRegistry"
  | "TopCv"
  | "LinkedIn"
  | "News"
  | "ExternalWebsite"
  | "SearchResult";

export type ResearchMode = "Initial" | "Refresh" | "Monitoring" | "TargetedEnrichment";

export type ResearchTarget =
  | "LegalIdentity"
  | "TaxRegistration"
  | "FoundedHistory"
  | "Industry"
  | "EmployeeScale"
  | "ProductsServices"
  | "Markets"
  | "Leadership"
  | "Locations";

export interface ResearchRun {
  id: string;
  companyId: string;
  status: ResearchRunStatus;
  requestedSearchProvider: string;
  actualSearchProvider?: string | null;
  requestedCrawlerProvider: string;
  actualCrawlerProvider?: string | null;
  sourcesFound: number;
  sourcesSelected: number;
  sourcesCrawled: number;
  startedAt: string;
  completedAt?: string | null;
  error?: string | null;
  stage: ResearchStage;
  groundingMode?: GroundingMode | null;
  resolvedIdentityCandidateId?: string | null;
  researchHint?: string | null;
  queriesTotal: number;
  queriesCompleted: number;
  uniqueCandidates: number;
  recommendedCandidates: number;
  crawlTotal: number;
  crawlCompleted: number;
  crawlSucceeded: number;
  crawlFailed: number;
  documentsAdded: number;
  duplicatesSkipped: number;
  mode?: ResearchMode;
  baseProfileVersionId?: string | null;
  targets?: ResearchTarget[] | null;
}

export type GroundedEntityType = "ParentGroup" | "Company" | "Subsidiary" | "Affiliate" | "Brand" | "Unknown";
export type GroundingConfidence = "Low" | "Medium" | "High";

export interface ResearchIdentityCandidate {
  id: string;
  researchRunId: string;
  temporaryId: string;
  displayName: string;
  legalName?: string | null;
  country?: string | null;
  website?: string | null;
  officialDomain?: string | null;
  entityType: GroundedEntityType;
  relationshipHint?: string | null;
  confidence: GroundingConfidence;
  rationale?: string | null;
  supportingCandidateIds: string[];
  recommended: boolean;
  selected: boolean;
  createdAt: string;
}

export interface ResearchCandidate {
  id: string;
  researchRunId: string;
  url: string;
  normalizedUrl: string;
  domain: string;
  title?: string | null;
  snippet?: string | null;
  sourceKind: SourceKind;
  recommendationReasons: string[];
  recommended: boolean;
  selected: boolean;
  acquisitionStatus: "Pending" | "Acquiring" | "Acquired" | "Failed" | "DuplicateSkipped" | "Unavailable";
  acquisitionError?: string | null;
  iconUrl?: string | null;
  entityRelationship?: "SameEntity" | "Parent" | "Subsidiary" | "Affiliate" | "DifferentEntity" | "Uncertain" | null;
  semanticRelevance?: "High" | "Medium" | "Low" | null;
  semanticPurposes?: string[];
  semanticRationale?: string | null;
  discoveredAt: string;
}

export interface AcquireResearchCandidatesRequest {
  candidateIds: string[];
}

export interface SourceDocument {
  id: string;
  companyId: string;
  researchRunId: string;
  url: string;
  normalizedUrl?: string | null;
  title?: string | null;
  sourceDomain?: string | null;
  sourceKind: SourceKind;
  iconUrl?: string | null;
  structuredFactsJson?: string | null;
  retrievedAt: string;
  crawlerProvider: string;
  contentPreview?: string | null;
  content?: string | null;
}
