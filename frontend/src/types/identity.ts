/**
 * Identity resolution is deliberately separate from research evidence. These
 * values describe a possible target and must never be presented as verified
 * Company Profile facts.
 */
export type IdentityQueryInterpretation =
  | "SpecificEntity"
  | "CorporateFamilyShorthand"
  | "NameCollision"
  | "Unknown";

export type IdentityResolutionStatus =
  | "Resolved"
  | "Ambiguous"
  | "NeedsMoreInfo"
  | "Unknown";

export type IdentityAmbiguityType =
  | "None"
  | "CorporateFamily"
  | "NameCollision"
  | "Unclear";

export type IdentityResolutionMethod =
  | "ExplicitIdentifier"
  | "ModelKnowledge"
  | "UserConfirmedExactInput"
  | "AcceptedExistingIdentity";

export type IdentityHintKind =
  | "Country"
  | "Website"
  | "LegalName"
  | "RegistrationNumber"
  | "Headquarters"
  | "Region"
  | "ResearchHint";

export type IdentityEntityType =
  | "ParentGroup"
  | "Company"
  | "Subsidiary"
  | "Affiliate"
  | "Brand"
  | "Unknown";

export type IdentityRelationshipToQuery =
  | "Exact"
  | "Alias"
  | "Parent"
  | "Subsidiary"
  | "SimilarName"
  | "Possible";

export type IdentityConfidence = "Low" | "Medium" | "High";

export interface IdentityResolutionRequest {
  name: string;
  legalName?: string | null;
  website?: string | null;
  country?: string | null;
  registrationNumber?: string | null;
  headquarters?: string | null;
  researchHint?: string | null;
  confirmExactName?: boolean;
  allowModelKnowledge?: boolean;
  guidedRefinement?: boolean;
  guidanceContext?: string | null;
}

export interface IdentityOption {
  temporaryId: string;
  displayName: string;
  legalName?: string | null;
  country?: string | null;
  region?: string | null;
  officialDomain?: string | null;
  entityType: IdentityEntityType;
  parentTemporaryId?: string | null;
  relationshipToQuery: IdentityRelationshipToQuery;
  confidence: IdentityConfidence;
  shortDescription?: string | null;
}

export interface IdentityResolutionResponse {
  status: IdentityResolutionStatus;
  ambiguityType: IdentityAmbiguityType;
  recommendedEntityId?: string | null;
  entities: IdentityOption[];
  requestedHints: IdentityHintKind[];
  message?: string | null;
  resolutionMethod: IdentityResolutionMethod;
  modelUsed?: string | null;
  warning?: string | null;
}

/** A bounded identity snapshot carried into the research handoff. */
export interface ResolvedIdentitySnapshot {
  displayName: string;
  country?: string | null;
  region?: string | null;
  legalNameHint?: string | null;
  officialDomainHint?: string | null;
  entityType: IdentityEntityType;
  parentName?: string | null;
  resolutionMethod: IdentityResolutionMethod;
}

export type IdentityWorkflowState =
  | "idle"
  | "resolving"
  | "resolved"
  | "ambiguous"
  | "needsMoreInfo"
  | "unknown"
  | "failed";

export type IdentityInputField = keyof IdentityResolutionRequest;
