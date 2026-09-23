import { request } from "./client";

export type GroundingMode = "Auto" | "Always" | "Off";
export type ProviderPreset = "Resilient" | "LocalFirst" | "Cloud" | "Custom";
export type ManagedResearchDepth = "Adaptive" | "Focused" | "Standard" | "Thorough" | "Exhaustive";

export interface ResearchSettings {
  groundingMode: GroundingMode;
  profileModel: string;
  chatModel: string;
  groundingModel: string;
  deepResearchModel: string;
  aiSourceRerankingEnabled: boolean;
  providerPreset: ProviderPreset;
  searchProviderPriority: string[];
  crawlerProviderPriority: string[];
  customSearchProviderPriority: string[];
  customCrawlerProviderPriority: string[];
  managedResearchProvider: string;
  managedResearchDepth: ManagedResearchDepth;
  updatedAt: string;
}

export type UpdateResearchSettings = Omit<ResearchSettings, "updatedAt">;

export interface GeminiModelOption {
  id: string;
  displayName: string;
  description: string;
  availability: "Available" | "Unavailable" | "Unverified";
}

export interface GeminiModelCatalog {
  projectAvailabilityVerified: boolean;
  message: string;
  models: GeminiModelOption[];
}

export function getResearchSettings() {
  return request<ResearchSettings>("/api/settings/research");
}

export function getGeminiModelCatalog() {
  return request<GeminiModelCatalog>("/api/settings/ai-models");
}

export function updateResearchSettings(settings: UpdateResearchSettings) {
  return request<ResearchSettings>("/api/settings/research", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(settings),
  });
}

export function resetResearchSettings() {
  return request<ResearchSettings>("/api/settings/research/reset", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
  });
}
