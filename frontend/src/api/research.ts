import { request } from "./client";

export interface ResearchRun {
  id: string;
  companyId: string;
  status: "Searching" | "Crawling" | "Completed" | "Failed";
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
}

export interface SourceDocument {
  id: string;
  companyId: string;
  researchRunId: string;
  url: string;
  title?: string | null;
  sourceDomain?: string | null;
  retrievedAt: string;
  crawlerProvider: string;
  contentPreview: string;
}

export function startResearch(companyId: string) {
  return request<ResearchRun>(`/api/companies/${encodeURIComponent(companyId)}/research`, { method: "POST" });
}

export function getResearchRun(researchRunId: string) {
  return request<ResearchRun>(`/api/research-runs/${encodeURIComponent(researchRunId)}`);
}

export function getCompanySources(companyId: string) {
  return request<SourceDocument[]>(`/api/companies/${encodeURIComponent(companyId)}/sources`);
}
