import { request } from "./client";

export interface ProviderStatus {
  provider: string;
  configured: boolean;
  available?: boolean | null;
  selectedModel?: string | null;
}

export interface ProviderStatusResponse {
  brave: ProviderStatus;
  crawl4Ai: ProviderStatus;
  gemini: ProviderStatus;
  deepResearchModel: string;
}

export interface RuntimeModelPreferences {
  fastModel: string;
  deepModel: string;
}

export function getProviderStatus() {
  return request<ProviderStatusResponse>("/api/system/provider-status");
}

export function updateModelPreferences(preferences: RuntimeModelPreferences) {
  return request<RuntimeModelPreferences>("/api/system/model-preferences", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(preferences),
  });
}
