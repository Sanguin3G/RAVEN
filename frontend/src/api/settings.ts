import { request } from "./client";

export type GroundingMode = "Auto" | "Always" | "Off";
export type ProviderPreset = "Balanced" | "LocalFirst" | "Cloud" | "Custom";

export interface ResearchSettings {
  groundingMode: GroundingMode;
  profileModel: string;
  groundingModel: string;
  deepResearchModel: string;
  aiSourceRerankingEnabled: boolean;
  providerPreset: ProviderPreset;
  searchProviderPriority: string[];
  crawlerProviderPriority: string[];
  updatedAt: string;
}

export type UpdateResearchSettings = Omit<ResearchSettings, "updatedAt">;

export function getResearchSettings() {
  return request<ResearchSettings>("/api/settings/research");
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
