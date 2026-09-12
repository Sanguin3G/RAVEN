import type { FormEvent, RefObject } from "react";
import type { ResearchActivityCounters } from "../../components/sources";
import type { Company, CompanyMatchResponse } from "../../types/company";
import type {
  GroundingMode,
  ResearchCandidate,
  ResearchIdentityCandidate,
  ResearchRun,
  ResearchTarget,
  SourceDocument,
} from "../../types/research";
import type { EvidenceCoverageResponse } from "../../api/coverage";
import type { CompanyProfileCandidate } from "../../types/profile";
import type { IdentityResolutionResponse } from "../../types/identity";

export type IdentityForm = {
  name: string;
  legalName: string;
  website: string;
  country: string;
  registrationNumber: string;
  headquarters: string;
  researchHint: string;
};

export type WorkspaceView =
  | "identify"
  | "matching"
  | "checkingIdentity"
  | "preflightIdentity"
  | "guidedIdentity"
  | "discovering"
  | "resolvingIdentity"
  | "reviewingSources"
  | "acquiring"
  | "reviewingEvidence"
  | "generatingProfile"
  | "reviewingProfile"
  | "completed"
  | "failed"
  | "cancelled";

export type GroundingOverride = "default" | GroundingMode;

export type CompanyResearchWorkflow = {
  form: IdentityForm;
  groundingOverride: GroundingOverride;
  setGroundingOverride: (value: GroundingOverride) => void;
  defaultGroundingMode: GroundingMode;
  view: WorkspaceView;
  setView: (value: WorkspaceView) => void;
  company: Company | null;
  run: ResearchRun | null;
  matches: CompanyMatchResponse[];
  preflightResponse: IdentityResolutionResponse | null;
  identityGuidance: IdentityResolutionResponse | null;
  selectedPreflightEntityId: string | null;
  setSelectedPreflightEntityId: (value: string | null) => void;
  identityCandidates: ResearchIdentityCandidate[];
  selectedIdentityCandidateId: string | null;
  setSelectedIdentityCandidateId: (value: string | null) => void;
  alternateIdentityHint: string;
  setAlternateIdentityHint: (value: string) => void;
  candidates: ResearchCandidate[];
  sources: SourceDocument[];
  coverage: EvidenceCoverageResponse | null;
  strengtheningTargets: ResearchTarget[];
  toggleStrengtheningTarget: (target: ResearchTarget) => void;
  loading: boolean;
  error: string | null;
  selectionError: string | null;
  profileCandidate: CompanyProfileCandidate | null;
  profileWarnings: string[];
  isPaused: boolean;
  identityHeadingRef: RefObject<HTMLHeadingElement | null>;
  selectedCount: number;
  coverageGaps: ResearchTarget[];
  activityCounters?: ResearchActivityCounters;
  canPause: boolean;
  researchStatusDetail: string | null;
  updateField: (field: keyof IdentityForm, value: string) => void;
  handleIdentitySubmit: (event: FormEvent<HTMLFormElement>) => Promise<void>;
  handlePreflightSelection: () => Promise<void>;
  requestPreflightClarification: () => Promise<void>;
  returnToIdentityChoices: () => void;
  retryGuidedIdentity: () => Promise<void>;
  retryPreflightIdentity: () => Promise<void>;
  researchExactName: () => Promise<void>;
  handleResearchExisting: (company: Company) => Promise<void>;
  createAndDiscover: () => Promise<void>;
  handleSelectIdentityCandidate: () => Promise<void>;
  handleContinueWithoutGrounding: () => Promise<void>;
  handleAlternateIdentitySearch: () => Promise<void>;
  togglePause: () => void;
  cancelCurrentResearch: () => Promise<void>;
  focusIdentityForm: () => void;
  updateCandidateSelection: (id: string, selected: boolean) => void;
  handleAcquire: () => Promise<void>;
  handleStrengthenDossier: () => Promise<void>;
  handleGenerateProfile: () => Promise<void>;
  handleConfirmProfile: () => Promise<void>;
  resetAfterFailure: () => void;
};
