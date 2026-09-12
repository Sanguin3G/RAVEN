import { request } from "./client";
import type { IdentityResolutionRequest, IdentityResolutionResponse } from "../types/identity";

function optional(value: string | null | undefined) {
  const trimmed = value?.trim();
  return trimmed ? trimmed : undefined;
}

/**
 * Keep the preflight payload limited to identity hints. In particular, this
 * endpoint must never receive search candidates or crawled evidence.
 */
export function toIdentityResolutionRequest(input: IdentityResolutionRequest): IdentityResolutionRequest {
  return {
    name: input.name.trim(),
    legalName: optional(input.legalName),
    website: optional(input.website),
    country: optional(input.country),
    registrationNumber: optional(input.registrationNumber),
    headquarters: optional(input.headquarters),
    researchHint: optional(input.researchHint),
    confirmExactName: input.confirmExactName || undefined,
    allowModelKnowledge: input.allowModelKnowledge,
    guidedRefinement: input.guidedRefinement || undefined,
  };
}

export function resolveCompanyIdentity(input: IdentityResolutionRequest) {
  const body = toIdentityResolutionRequest(input);
  return request<IdentityResolutionResponse>("/api/research/identity/resolve", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
}
