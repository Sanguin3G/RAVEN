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
  exa: ProviderStatus;
  firecrawl: ProviderStatus;
  deepResearchModel: string;
}

export interface RuntimeModelPreferences {
  fastModel: string;
  deepModel: string;
}

export function getProviderStatus() {
  return request<ProviderStatusResponse>("/api/system/provider-status");
}

export async function getApiHealth() {
  const configuredApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim();
  const baseUrl = configuredApiBaseUrl ? configuredApiBaseUrl.replace(/\/$/, "") : "";
  const healthPath = baseUrl ? `${baseUrl}/health` : "/api/health";
  try {
    const response = await fetch(healthPath, { headers: { Accept: "text/plain" } });
    return { available: response.ok };
  } catch {
    return { available: false };
  }
}

export function updateModelPreferences(preferences: RuntimeModelPreferences) {
  return request<RuntimeModelPreferences>("/api/system/model-preferences", {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(preferences),
  });
}
