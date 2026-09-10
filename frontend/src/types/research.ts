export type ResearchRunStatus = "Searching" | "Crawling" | "Completed" | "Failed";

export type ResearchStage =
  | "Identifying"
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
  | "BusinessRegistry"
  | "TopCv"
  | "LinkedIn"
  | "News"
  | "ExternalWebsite"
  | "SearchResult";

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
