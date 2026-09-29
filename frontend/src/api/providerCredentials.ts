import { request } from "./client";

export type ProviderCredentialName = "brave" | "exa" | "gemini" | "google-maps" | "crawl4ai";

export interface ProviderCredentialStatus {
  provider: ProviderCredentialName;
  configured: boolean;
  source: "workspace" | "environment" | "missing";
}

export interface ProviderCredentialAdministrationResponse {
  credentials: ProviderCredentialStatus[];
  workspaceOverridesAvailable: boolean;
}

export interface ProviderCredentialReplacement {
  apiKey?: string;
  endpoint?: string;
  token?: string;
}

export interface ProviderCredentialOperationResponse {
  succeeded: boolean;
  message: string;
}

export function getProviderCredentials() {
  return request<ProviderCredentialAdministrationResponse>("/api/admin/provider-credentials");
}

export function replaceProviderCredential(provider: ProviderCredentialName, value: ProviderCredentialReplacement) {
  return request<ProviderCredentialOperationResponse>(`/api/admin/provider-credentials/${provider}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(value),
  });
}

export function removeProviderCredential(provider: ProviderCredentialName) {
  return request<ProviderCredentialOperationResponse>(`/api/admin/provider-credentials/${provider}`, { method: "DELETE" });
}

export function testProviderCredential(provider: ProviderCredentialName) {
  return request<ProviderCredentialOperationResponse>(`/api/admin/provider-credentials/${provider}/test`, { method: "POST" });
}
