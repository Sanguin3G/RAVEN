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

export function getProviderStatus() {
  return request<ProviderStatusResponse>("/api/system/provider-status");
}
