import type { SourceKind } from "../sources/sourceUtils";
import type { CompanyProfileVersion } from "../../types/profile";
import type { ProfileChange } from "../../api/profileTracking";
import type { CompanyMonitoring, UpdateCompanyMonitoring } from "../../api/monitoring";
import type { EvidenceCoverageResponse } from "../../api/coverage";
import type { ResearchTarget } from "../../api/coverage";
import type { SavedResearchArtifact } from "../../api/investigations";

export type DossierTab = "overview" | "sources" | "investigations" | "changes" | "monitoring";

export interface DossierCompany {
  id: string;
  displayName: string;
  legalName?: string | null;
  registrationNumber?: string | null;
  website?: string | null;
  country?: string | null;
  headquarters?: string | null;
  industry?: string | null;
  lastResearchedAt?: string | null;
}

export interface DossierProduct {
  name: string;
  type?: string | null;
  description?: string | null;
}

export interface DossierMarket {
  name: string;
  type?: string | null;
}

export interface DossierLeader {
  name: string;
  title?: string | null;
}

export interface DossierLocation {
  name?: string | null;
  address?: string | null;
  country?: string | null;
  type?: string | null;
}

/**
 * Presentational profile data. It intentionally does not mirror an API DTO:
 * the dossier can render a generated preview and an accepted version equally.
 */
export interface DossierProfile {
  id?: string;
  legalName?: string | null;
  website?: string | null;
  country?: string | null;
  headquarters?: string | null;
  registrationNumberOrTaxId?: string | null;
  foundedYear?: number | null;
  primaryIndustry?: string | null;
  secondaryIndustries?: string[] | null;
  companySize?: string | null;
  employeeCount?: number | null;
  employeeCountRange?: string | null;
  summary?: string | null;
  productsServices?: DossierProduct[] | null;
  markets?: DossierMarket[] | null;
  leadership?: DossierLeader[] | null;
  locations?: DossierLocation[] | null;
  publicLinks?: string[] | null;
  generatedAt?: string | null;
  confirmedAt?: string | null;
  researchRunId?: string | null;
  version?: number | null;
  evidenceCount?: number | null;
}

export type DossierSourceStatus = "acquired" | "failed" | "duplicate";

export interface DossierSource {
  id: string;
  url: string;
  title?: string | null;
  domain?: string | null;
  kind?: SourceKind | null;
  iconUrl?: string | null;
  preview?: string | null;
  retrievedAt?: string | null;
  crawlerProvider?: string | null;
  status?: DossierSourceStatus;
  statusMessage?: string | null;
}

export type DossierResearchStatus = "idle" | "working" | "waiting" | "failed" | "completed";
export type DossierResearchStepState = "completed" | "active" | "waiting" | "failed" | "upcoming";

export interface DossierResearchStep {
  id: string;
  label: string;
  detail?: string | null;
  state: DossierResearchStepState;
}

export interface DossierResearchCounter {
  label: string;
  value: string | number;
}

export interface DossierResearch {
  status: DossierResearchStatus;
  stageLabel?: string | null;
  summary?: string | null;
  provider?: string | null;
  model?: string | null;
  runId?: string | null;
  error?: string | null;
  steps?: DossierResearchStep[] | null;
  counters?: DossierResearchCounter[] | null;
}

export interface DossierTracking {
  versions: CompanyProfileVersion[];
  changes: ProfileChange[];
  isLoading?: boolean;
  error?: string | null;
  isRefreshing?: boolean;
  onRefreshResearch?: () => void | Promise<void>;
}

export interface DossierMonitoring {
  monitoring: CompanyMonitoring;
  isLoading?: boolean;
  isSaving?: boolean;
  error?: string | null;
  onUpdate?: (update: UpdateCompanyMonitoring) => void | Promise<void>;
  onResearchNow?: () => void | Promise<void>;
}

export interface DossierCoverage {
  response?: EvidenceCoverageResponse | null;
  isLoading?: boolean;
  error?: string | null;
}

export interface DossierInvestigations {
  artifacts?: SavedResearchArtifact[] | null;
  isLoading?: boolean;
  error?: string | null;
  onRefresh?: () => void | Promise<void>;
}

export interface CompanyDossierProps {
  company: DossierCompany;
  profile?: DossierProfile | null;
  sources?: DossierSource[] | null;
  research?: DossierResearch | null;
  activeTab?: DossierTab;
  initialTab?: DossierTab;
  onTabChange?: (tab: DossierTab) => void;
  tracking?: DossierTracking | null;
  monitoring?: DossierMonitoring | null;
  coverage?: DossierCoverage | null;
  investigations?: DossierInvestigations | null;
  initialEnrichmentTargets?: ResearchTarget[];
  openEnrichment?: boolean;
  onProfileConfirmed?: (profile: CompanyProfileVersion) => void;
}
