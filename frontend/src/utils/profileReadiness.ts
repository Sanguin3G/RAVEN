export interface ProfileReadinessSource {
  aiProvider?: string | null;
  aiModel?: string | null;
  promptTemplateVersion?: string | null;
  legalName?: string | null;
  registrationNumberOrTaxId?: string | null;
  foundedYear?: number | null;
  primaryIndustry?: string | null;
  secondaryIndustries?: string[] | null;
  companySize?: string | null;
  employeeCount?: number | null;
  employeeCountRange?: string | null;
  productsServices?: unknown[] | null;
  markets?: unknown[] | null;
  leadership?: unknown[] | null;
  locations?: unknown[] | null;
}

export type DossierProfileReadiness = "missing" | "identity-only" | "sparse" | "partial" | "complete";

const requiredTargetCount = 9;
const partialMinimumCoveredTargets = 3;

function hasModelCreationProvenance(profile: ProfileReadinessSource) {
  return Boolean(profile.aiProvider?.trim() && profile.aiModel?.trim() && profile.promptTemplateVersion?.trim());
}

export function countProfileCoveredTargets(profile: ProfileReadinessSource) {
  let covered = 0;
  if (profile.legalName?.trim()) covered += 1;
  if (profile.registrationNumberOrTaxId?.trim()) covered += 1;
  if (profile.foundedYear !== null && profile.foundedYear !== undefined) covered += 1;
  if (profile.primaryIndustry?.trim() || (profile.secondaryIndustries?.length ?? 0) > 0) covered += 1;
  if (profile.companySize?.trim() || (profile.employeeCount !== null && profile.employeeCount !== undefined) || profile.employeeCountRange?.trim()) covered += 1;
  if ((profile.productsServices?.length ?? 0) > 0) covered += 1;
  if ((profile.markets?.length ?? 0) > 0) covered += 1;
  if ((profile.leadership?.length ?? 0) > 0) covered += 1;
  if ((profile.locations?.length ?? 0) > 0) covered += 1;
  return covered;
}

export function getProfileReadiness(profile?: ProfileReadinessSource | null): DossierProfileReadiness {
  if (!profile) return "missing";
  const covered = countProfileCoveredTargets(profile);
  if (covered === 0) return "identity-only";
  if (covered >= requiredTargetCount) return "complete";
  if (covered >= partialMinimumCoveredTargets) return "partial";
  return "sparse";
}

/**
 * A version must be a model-created, evidence-bearing profile with at least
 * one supported research target before it can be used as a patch baseline.
 * The identity-only row left by an interrupted run is intentionally excluded.
 */
export function hasUsableAcceptedProfile(profile?: ProfileReadinessSource | null, gapCount?: number) {
  return Boolean(
    profile
    && hasModelCreationProvenance(profile)
    && countProfileCoveredTargets(profile) > 0
    && (gapCount === undefined || gapCount < requiredTargetCount),
  );
}

export function profileHasModelCreationProvenance(profile?: ProfileReadinessSource | null) {
  return Boolean(profile && hasModelCreationProvenance(profile));
}
