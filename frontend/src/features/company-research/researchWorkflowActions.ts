import type { Dispatch, SetStateAction } from "react";
import { acquireResearchCandidates, getResearchCandidates, getResearchSources, startBackgroundResearch } from "../../api/research";
import { getResearchRunCoverage } from "../../api/coverage";
import { generateCompanyProfile, confirmCompanyProfile } from "../../api/profiles";
import { startManagedResearch } from "../../api/managedResearch";
import { getApiErrorMessage } from "../../api/client";
import type { Company } from "../../types/company";
import type { ResearchCandidate, ResearchRun, ResearchTarget, SourceDocument } from "../../types/research";
import type { CompanyProfileCandidate } from "../../types/profile";
import type { EvidenceCoverageResponse } from "../../api/coverage";
import type { WorkspaceView } from "./types";
import { rememberCurrentResearch } from "../../utils/researchSession";
import { clearCurrentResearch } from "../../utils/researchSession";
import { upsertResearchActivity } from "../../utils/researchActivity";

type Setter<T> = Dispatch<SetStateAction<T>>;

export type ResearchEvidenceActionContext = {
  run: ResearchRun | null;
  candidates: ResearchCandidate[];
  company: Company | null;
  strengtheningTargets: ResearchTarget[];
  profileCandidate: CompanyProfileCandidate | null;
  isPaused: boolean;
  setRun: Setter<ResearchRun | null>;
  setCandidates: Setter<ResearchCandidate[]>;
  setSources: Setter<SourceDocument[]>;
  setCoverage: Setter<EvidenceCoverageResponse | null>;
  setStrengtheningTargets: Setter<ResearchTarget[]>;
  setProfileCandidate: Setter<CompanyProfileCandidate | null>;
  setProfileWarnings: Setter<string[]>;
  setLoading: Setter<boolean>;
  setError: Setter<string | null>;
  setNotice: Setter<string | null>;
  setSelectionError: Setter<string | null>;
  setIsPaused: Setter<boolean>;
  setView: Setter<WorkspaceView>;
  waitForDiscovery: (runId: string) => Promise<ResearchRun>;
};

export function createResearchEvidenceActions(context: ResearchEvidenceActionContext) {
  const {
    run,
    candidates,
    company,
    strengtheningTargets,
    profileCandidate,
    isPaused,
    setRun,
    setCandidates,
    setSources,
    setCoverage,
    setStrengtheningTargets,
    setProfileCandidate,
    setProfileWarnings,
    setLoading,
    setError,
    setNotice,
    setSelectionError,
    setIsPaused,
    setView,
    waitForDiscovery,
  } = context;

  async function handleAcquire() {
    if (!run) return;
    const selectedIds = candidates.filter((candidate) => candidate.selected).map((candidate) => candidate.id);
    if (selectedIds.length === 0) {
      setSelectionError("Select at least one discovered source before acquiring.");
      return;
    }

    setSelectionError(null);
    setError(null);
    setLoading(true);
    setView("acquiring");

    try {
      const nextRun = await acquireResearchCandidates(run.id, selectedIds);
      setRun(nextRun);
      if (nextRun.stage === "Failed") {
        setError(nextRun.error || "RAVEN could not acquire the selected sources.");
        setView("failed");
        return;
      }
      const [nextCandidates, nextSources] = await Promise.all([
        getResearchCandidates(run.id),
        getResearchSources(run.id),
      ]);
      setCandidates(nextCandidates);
      setSources(nextSources);
      const nextCoverage = await getResearchRunCoverage(nextRun.id).catch(() => null);
      const usableCoverage = nextCoverage && Array.isArray(nextCoverage.items) ? nextCoverage : null;
      setCoverage(usableCoverage);
      setStrengtheningTargets((usableCoverage?.items ?? [])
        .filter((item) => item.level === "Missing" || item.level === "Weak")
        .map((item) => item.target));
      setView("reviewingEvidence");
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "RAVEN could not acquire the selected sources."));
      setView("failed");
    } finally {
      setLoading(false);
    }
  }

  async function handleStrengthenDossier() {
    if (!company || strengtheningTargets.length === 0) return;
    setLoading(true);
    setError(null);
    setView("discovering");
    try {
      const queuedRun = await startBackgroundResearch(company.id, undefined, undefined, true, {
        mode: "TargetedEnrichment",
        targets: strengtheningTargets,
      });
      setRun(queuedRun);
      rememberCurrentResearch(queuedRun, company.name);
      setIsPaused(false);
      const nextRun = await waitForDiscovery(queuedRun.id);
      setRun(nextRun);
      const nextCandidates = await getResearchCandidates(nextRun.id);
      setCandidates(nextCandidates.map((candidate) => ({ ...candidate, selected: candidate.selected || candidate.recommended })));
      setSources([]);
      setCoverage(null);
      setView("reviewingSources");
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "RAVEN could not start dossier strengthening."));
      setView("failed");
    } finally {
      setLoading(false);
    }
  }

  async function handleGenerateProfile() {
    if (!run) return;
    setError(null);
    setLoading(true);
    setView("generatingProfile");
    try {
      const generated = await generateCompanyProfile(run.id);
      setProfileWarnings(generated.warnings);
      if (!generated.candidate) {
        setError(generated.failure?.message || "RAVEN could not generate a validated Company Profile.");
        setView("failed");
        return;
      }
      setProfileCandidate(generated.candidate);
      setView("reviewingProfile");
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "RAVEN could not generate a Company Profile."));
      setView("failed");
    } finally {
      setLoading(false);
    }
  }

  async function handleConfirmProfile() {
    if (!run || !profileCandidate) return;
    setError(null);
    setLoading(true);
    try {
      await confirmCompanyProfile(run.id, profileCandidate.id);
      clearCurrentResearch(run.id);
      setIsPaused(false);
      setView("completed");
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "RAVEN could not confirm this Company Profile."));
      setView("failed");
    } finally {
      setLoading(false);
    }
  }

  async function handleDeepResearch() {
    if (!company || strengtheningTargets.length === 0) return;
    setLoading(true);
    setError(null);
    setNotice(null);
    const objective = `Strengthen the company profile with current evidence for ${strengtheningTargets.join(", ")}.`;
    try {
      const job = await startManagedResearch(company.id, objective, { purpose: "ProfileImprovement" });
      upsertResearchActivity({
        id: `deep-${job.id}`,
        jobId: job.id,
        origin: "Deep",
        companyId: company.id,
        companyName: company.name,
        objective,
        detail: job.status === "Completed" ? "Ready for review" : "Researching across sources",
        status: job.status === "Completed" ? "ready" : "running",
        locked: job.status === "Completed",
        href: `/companies/${encodeURIComponent(company.id)}?tab=investigations&research=${encodeURIComponent(job.investigationId ?? job.id)}`,
        updatedAt: job.completedAt || job.createdAt,
      });
      setNotice("Deep Research started · running in the background. Results will appear in Investigations.");
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "Deep Research could not be started."));
    } finally {
      setLoading(false);
    }
  }

  return { handleAcquire, handleStrengthenDossier, handleGenerateProfile, handleConfirmProfile, handleDeepResearch };
}
