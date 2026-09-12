import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { useSearchParams } from "react-router-dom";
import { getApiErrorMessage } from "../../api/client";
import { getCompanyProfileCandidate, generateCompanyProfile, confirmCompanyProfile } from "../../api/profiles";
import { createCompany, findCompanyMatches, getCompany } from "../../api/companies";
import {
  acquireResearchCandidates,
  cancelResearchRun,
  getResearchCandidates,
  getResearchIdentityCandidates,
  getResearchRun,
  getResearchSources,
  selectResearchIdentityCandidate,
  startBackgroundResearch,
} from "../../api/research";
import { getResearchRunCoverage, type EvidenceCoverageResponse } from "../../api/coverage";
import { getResearchSettings } from "../../api/settings";
import { resolveCompanyIdentity } from "../../api/identity";
import type { Company, CompanyMatchResponse, CreateCompanyRequest } from "../../types/company";
import type { GroundingMode, ResearchCandidate, ResearchIdentityCandidate, ResearchRun, ResearchTarget, SourceDocument } from "../../types/research";
import type { CompanyProfileCandidate } from "../../types/profile";
import type { IdentityOption, IdentityResolutionResponse, ResolvedIdentitySnapshot } from "../../types/identity";
import { canPauseResearchStage, researchProgressLabel } from "../../utils/researchProgress";
import { clearCurrentResearch, readCurrentResearch, rememberCurrentResearch, setCurrentResearchPaused } from "../../utils/researchSession";
import type { CompanyResearchWorkflow, GroundingOverride, IdentityForm, WorkspaceView } from "./types";

export const initialForm: IdentityForm = {
  name: "",
  legalName: "",
  website: "",
  country: "",
  registrationNumber: "",
  headquarters: "",
  researchHint: "",
};

function optional(value: string) {
  const trimmed = value.trim();
  return trimmed ? trimmed : undefined;
}

function companyRequest(form: IdentityForm): CreateCompanyRequest {
  return {
    name: form.name.trim(),
    legalName: optional(form.legalName),
    website: optional(form.website),
    country: optional(form.country),
    registrationNumber: optional(form.registrationNumber),
    headquarters: optional(form.headquarters),
  };
}

export function useCompanyResearchWorkflow(): CompanyResearchWorkflow {
  const [searchParams] = useSearchParams();
  const [form, setForm] = useState<IdentityForm>(initialForm);
  const [groundingOverride, setGroundingOverride] = useState<GroundingOverride>("default");
  const [defaultGroundingMode, setDefaultGroundingMode] = useState<GroundingMode>("Auto");
  const [view, setView] = useState<WorkspaceView>("identify");
  const [company, setCompany] = useState<Company | null>(null);
  const [run, setRun] = useState<ResearchRun | null>(null);
  const [matches, setMatches] = useState<CompanyMatchResponse[]>([]);
  const [preflightResponse, setPreflightResponse] = useState<IdentityResolutionResponse | null>(null);
  const [selectedPreflightEntityId, setSelectedPreflightEntityId] = useState<string | null>(null);
  const [pendingResolvedIdentity, setPendingResolvedIdentity] = useState<ResolvedIdentitySnapshot | null>(null);
  const [identityCandidates, setIdentityCandidates] = useState<ResearchIdentityCandidate[]>([]);
  const [selectedIdentityCandidateId, setSelectedIdentityCandidateId] = useState<string | null>(null);
  const [alternateIdentityHint, setAlternateIdentityHint] = useState("");
  const [candidates, setCandidates] = useState<ResearchCandidate[]>([]);
  const [sources, setSources] = useState<SourceDocument[]>([]);
  const [coverage, setCoverage] = useState<EvidenceCoverageResponse | null>(null);
  const [strengtheningTargets, setStrengtheningTargets] = useState<ResearchTarget[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selectionError, setSelectionError] = useState<string | null>(null);
  const [profileCandidate, setProfileCandidate] = useState<CompanyProfileCandidate | null>(null);
  const [profileWarnings, setProfileWarnings] = useState<string[]>([]);
  const [isPaused, setIsPaused] = useState(false);
  const identityHeadingRef = useRef<HTMLHeadingElement>(null);
  const refreshStartedRef = useRef(false);
  const restoredRunRef = useRef<string | null>(null);

  useEffect(() => {
    getResearchSettings()
      .then((settings) => {
        if (["Auto", "Always", "Off"].includes(settings.groundingMode)) setDefaultGroundingMode(settings.groundingMode);
      })
      .catch(() => undefined);
  }, []);

  async function waitForDiscovery(runId: string) {
    let current = await getResearchRun(runId);
    setRun(current);
    while (["Identifying", "Discovering", "Grounding"].includes(current.stage) && current.status !== "Failed" && current.status !== "Cancelled") {
      await new Promise((resolve) => window.setTimeout(resolve, 900));
      current = await getResearchRun(runId);
      setRun(current);
    }
    return current;
  }

  async function waitForAcquisition(runId: string) {
    let current = await getResearchRun(runId);
    setRun(current);
    while (current.stage === "Acquiring" && current.status !== "Failed" && current.status !== "Cancelled") {
      await new Promise((resolve) => window.setTimeout(resolve, 900));
      current = await getResearchRun(runId);
      setRun(current);
    }
    return current;
  }

  const selectedCount = useMemo(() => candidates.filter((candidate) => candidate.selected).length, [candidates]);
  const coverageGaps = useMemo(() => (coverage?.items ?? [])
    .filter((item) => item.level === "Missing" || item.level === "Weak")
    .map((item) => item.target), [coverage]);
  const activityCounters = run
    ? {
        queriesTotal: run.queriesTotal,
        queriesCompleted: run.queriesCompleted,
        searchResultsFound: run.sourcesFound,
        uniqueCandidates: run.uniqueCandidates,
        recommendedCandidates: run.recommendedCandidates,
        sourcesSelected: run.sourcesSelected,
        crawlTotal: run.crawlTotal,
        crawlCompleted: run.crawlCompleted,
        crawlSucceeded: run.crawlSucceeded,
        crawlFailed: run.crawlFailed,
        documentsAdded: run.documentsAdded,
        duplicatesSkipped: run.duplicatesSkipped,
      }
    : undefined;
  const canPause = run ? canPauseResearchStage(run.stage) : false;
  const researchStatusDetail = run ? researchProgressLabel(run, isPaused) : null;

  async function loadCandidatesForRun(researchRunId: string) {
    const nextCandidates = await getResearchCandidates(researchRunId);
    setCandidates(nextCandidates.map((candidate) => ({ ...candidate, selected: candidate.selected || candidate.recommended })));
  }

  async function loadEvidenceForRun(researchRunId: string) {
    const [nextCandidates, nextSources, nextCoverage] = await Promise.all([
      getResearchCandidates(researchRunId),
      getResearchSources(researchRunId),
      getResearchRunCoverage(researchRunId).catch(() => null),
    ]);
    setCandidates(nextCandidates);
    setSources(nextSources);
    const usableCoverage = nextCoverage && Array.isArray(nextCoverage.items) ? nextCoverage : null;
    setCoverage(usableCoverage);
    setStrengtheningTargets((usableCoverage?.items ?? [])
      .filter((item) => item.level === "Missing" || item.level === "Weak")
      .map((item) => item.target));
  }

  async function restoreResearchRun(researchRunId: string) {
    setLoading(true);
    setError(null);
    try {
      let restoredRun = await getResearchRun(researchRunId);
      const existingCompany = await getCompany(restoredRun.companyId);
      const savedSession = readCurrentResearch();
      const paused = savedSession?.runId === restoredRun.id && savedSession.paused && canPauseResearchStage(restoredRun.stage);

      setCompany(existingCompany);
      setForm({
        name: existingCompany.name,
        legalName: existingCompany.legalName || "",
        website: existingCompany.website || "",
        country: existingCompany.country || "",
        registrationNumber: existingCompany.registrationNumber || "",
        headquarters: existingCompany.headquarters || "",
        researchHint: restoredRun.researchHint || "",
      });
      setRun(restoredRun);
      setIsPaused(paused);
      rememberCurrentResearch(restoredRun, existingCompany.name, paused);

      if (!paused && ["Identifying", "Discovering", "Grounding"].includes(restoredRun.stage)) {
        setView("discovering");
        restoredRun = await waitForDiscovery(restoredRun.id);
      } else if (!paused && restoredRun.stage === "Acquiring") {
        setView("acquiring");
        restoredRun = await waitForAcquisition(restoredRun.id);
      }

      setRun(restoredRun);
      if (restoredRun.stage === "Completed" || restoredRun.stage === "Cancelled") {
        clearCurrentResearch(restoredRun.id);
        setIsPaused(false);
        setView("identify");
        return;
      }
      if (restoredRun.stage === "Failed") {
        setError(restoredRun.error || "RAVEN could not complete this research run.");
        setView("failed");
        return;
      }

      switch (restoredRun.stage) {
        case "AwaitingIdentitySelection": {
          const identity = await getResearchIdentityCandidates(restoredRun.id);
          setIdentityCandidates(identity);
          setSelectedIdentityCandidateId(identity.find((candidate) => candidate.recommended)?.id || identity.find((candidate) => candidate.selected)?.id || null);
          setView("resolvingIdentity");
          break;
        }
        case "AwaitingSourceSelection":
          await loadCandidatesForRun(restoredRun.id);
          setView("reviewingSources");
          break;
        case "EvidenceReady":
          await loadEvidenceForRun(restoredRun.id);
          setView("reviewingEvidence");
          break;
        case "GeneratingProfile":
          setView("generatingProfile");
          break;
        case "AwaitingProfileConfirmation": {
          const candidate = await getCompanyProfileCandidate(restoredRun.id);
          if (candidate) {
            setProfileCandidate(candidate);
            setView("reviewingProfile");
          } else {
            setError("The saved profile preview is no longer available. Generate it again from the acquired evidence.");
            await loadEvidenceForRun(restoredRun.id);
            setView("reviewingEvidence");
          }
          break;
        }
        default:
          setView("discovering");
          break;
      }
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "RAVEN could not restore this research run."));
      setView("failed");
    } finally {
      setLoading(false);
    }
  }

  function updateField(field: keyof IdentityForm, value: string) {
    setForm((current) => ({ ...current, [field]: value }));
    setError(null);
  }

  async function discoverForCompany(nextCompany: Company, useAcceptedProfileIdentity = false, researchHintOverride?: string, resolvedIdentity?: ResolvedIdentitySnapshot | null) {
    setCompany(nextCompany);
    setMatches([]);
    setError(null);
    setSelectionError(null);
    setIdentityCandidates([]);
    setSelectedIdentityCandidateId(null);
    setLoading(true);
    setView("discovering");

    try {
      const queuedRun = await startBackgroundResearch(
        nextCompany.id,
        researchHintOverride ?? form.researchHint,
        groundingOverride === "default" ? undefined : groundingOverride,
        useAcceptedProfileIdentity,
        { resolvedIdentity: resolvedIdentity || undefined },
      );
      setRun(queuedRun);
      rememberCurrentResearch(queuedRun, nextCompany.name);
      setIsPaused(false);
      const nextRun = await waitForDiscovery(queuedRun.id);
      setRun(nextRun);
      if (nextRun.stage === "Failed") {
        setError(nextRun.error || "RAVEN could not discover public sources.");
        setView("failed");
        return;
      }
      if (nextRun.stage === "Grounding" || nextRun.stage === "AwaitingIdentitySelection") {
        try {
          const groundedCandidates = await getResearchIdentityCandidates(nextRun.id);
          if (groundedCandidates.length > 0) {
            setIdentityCandidates(groundedCandidates);
            setSelectedIdentityCandidateId(groundedCandidates.find((candidate) => candidate.recommended)?.id || null);
            setView("resolvingIdentity");
            return;
          }
        } catch {
          // Grounding is assistive. An unavailable resolver must not block deterministic discovery.
        }
      }
      const discoveredCandidates = await getResearchCandidates(nextRun.id);
      setCandidates(discoveredCandidates.map((candidate) => ({ ...candidate, selected: candidate.selected || candidate.recommended })));
      setSources([]);
      setView("reviewingSources");
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "RAVEN could not discover public sources."));
      setView("failed");
    } finally {
      setLoading(false);
    }
  }

  async function createAndDiscover() {
    setLoading(true);
    setError(null);
    setView("discovering");

    try {
      const nextCompany = await createCompany(companyRequest(form));
      await discoverForCompany(nextCompany, false, undefined, pendingResolvedIdentity);
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "RAVEN could not create this company."));
      setView("failed");
      setLoading(false);
    }
  }

  async function handleIdentitySubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setSelectionError(null);
    setLoading(true);
    setView("checkingIdentity");

    try {
      const response = await resolveCompanyIdentity({ ...companyRequest(form), researchHint: optional(form.researchHint), allowModelKnowledge: groundingOverride !== "Off" });
      setPreflightResponse(response);
      const selected = response.recommendedEntityId ?? (response.entities.length === 1 ? response.entities[0].temporaryId : null);
      setSelectedPreflightEntityId(selected);
      if (response.status === "Resolved" && selected) {
        await continueResolvedIdentity(response, selected);
        return;
      }
      setView("preflightIdentity");
      setLoading(false);
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "RAVEN could not check this company identity."));
      setView("failed");
      setLoading(false);
    }
  }

  async function continueResolvedIdentity(response: IdentityResolutionResponse, entityId: string) {
    const entity = response.entities.find((item) => item.temporaryId === entityId);
    if (!entity) { setError("Choose the organization RAVEN should research."); setView("preflightIdentity"); setLoading(false); return; }
    const resolvedForm = { ...form, name: entity.displayName, country: form.country || entity.country || "" };
    const snapshot: ResolvedIdentitySnapshot = { displayName: entity.displayName, country: entity.country ?? null, region: entity.region ?? null, legalNameHint: entity.legalName ?? null, officialDomainHint: entity.officialDomain ?? null, entityType: entity.entityType, parentName: null, resolutionMethod: response.resolutionMethod };
    setForm(resolvedForm); setPendingResolvedIdentity(snapshot); setView("matching");
    const foundMatches = await findCompanyMatches(companyRequest(resolvedForm));
    if (foundMatches.length > 0) { setMatches(foundMatches); setLoading(false); return; }
    const nextCompany = await createCompany(companyRequest(resolvedForm));
    await discoverForCompany(nextCompany, false, undefined, snapshot);
  }

  async function handlePreflightSelection() {
    if (!preflightResponse || !selectedPreflightEntityId) { setError("Choose the organization RAVEN should research."); return; }
    setLoading(true); setError(null); await continueResolvedIdentity(preflightResponse, selectedPreflightEntityId);
  }

  function requestPreflightClarification() {
    setSelectedPreflightEntityId(null);
    setError(null);
    setPreflightResponse({
      status: "NeedsMoreInfo",
      ambiguityType: "Unclear",
      recommendedEntityId: null,
      entities: [],
      requestedHints: ["Country", "Website"],
      message: "Can't find the organization you mean? Add a country, website, or a more specific company name.",
      resolutionMethod: "ModelKnowledge",
    });
    setView("preflightIdentity");
  }

  async function retryPreflightIdentity() {
    setLoading(true); setError(null); setView("checkingIdentity");
    try {
      const response = await resolveCompanyIdentity({ ...companyRequest(form), researchHint: optional(form.researchHint), allowModelKnowledge: groundingOverride !== "Off" });
      setPreflightResponse(response); setSelectedPreflightEntityId(response.recommendedEntityId ?? (response.entities.length === 1 ? response.entities[0].temporaryId : null));
      if (response.status === "Resolved" && (response.recommendedEntityId || response.entities.length === 1)) await continueResolvedIdentity(response, response.recommendedEntityId || response.entities[0].temporaryId);
      else { setView("preflightIdentity"); setLoading(false); }
    } catch (reason: unknown) { setError(getApiErrorMessage(reason, "RAVEN couldn't confidently resolve this organization right now.")); setView("preflightIdentity"); setLoading(false); }
  }

  async function researchExactName() {
    setLoading(true); setError(null);
    try {
      const response = await resolveCompanyIdentity({ ...companyRequest(form), researchHint: optional(form.researchHint), confirmExactName: true, allowModelKnowledge: groundingOverride !== "Off" });
      setPreflightResponse(response); await continueResolvedIdentity(response, response.recommendedEntityId || "");
    } catch (reason: unknown) { setError(getApiErrorMessage(reason, "RAVEN couldn't start exact-name research.")); setLoading(false); }
  }

  async function handleResearchExisting(existingCompany: Company) {
    const identityHint = [
      form.country.trim() ? `Country: ${form.country.trim()}` : "",
      form.legalName.trim() ? `Legal name: ${form.legalName.trim()}` : "",
      form.researchHint.trim(),
    ].filter(Boolean).join("; ");
    await discoverForCompany(existingCompany, false, identityHint || undefined, pendingResolvedIdentity ?? undefined);
  }

  useEffect(() => {
    const requestedRunId = searchParams.get("researchRun") || (!searchParams.get("refreshCompanyId") ? readCurrentResearch()?.runId : null);
    if (!requestedRunId || restoredRunRef.current === requestedRunId) return;

    restoredRunRef.current = requestedRunId;
    void restoreResearchRun(requestedRunId);
    // The run identifier is the intentional restore key; restore only reads current server state.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchParams]);

  useEffect(() => {
    const refreshCompanyId = searchParams.get("refreshCompanyId");
    if (!refreshCompanyId || searchParams.get("researchRun") || refreshStartedRef.current) return;

    refreshStartedRef.current = true;
    getCompany(refreshCompanyId)
      .then((existingCompany) => {
        setForm({
          name: existingCompany.name,
          legalName: existingCompany.legalName || "",
          website: existingCompany.website || "",
          country: existingCompany.country || "",
          registrationNumber: existingCompany.registrationNumber || "",
          headquarters: existingCompany.headquarters || "",
          researchHint: "",
        });
        return discoverForCompany(existingCompany, true);
      })
      .catch((reason: unknown) => {
        setError(getApiErrorMessage(reason, "RAVEN could not prepare this company for a research refresh."));
        setView("failed");
      });
    // A refresh route is a one-shot entry action. The ref prevents rerunning it as local form state changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchParams]);

  async function handleSelectIdentityCandidate() {
    if (!run || !selectedIdentityCandidateId) {
      setSelectionError("Choose the company RAVEN should research before continuing.");
      return;
    }

    setSelectionError(null);
    setError(null);
    setLoading(true);
    setView("discovering");

    try {
      const nextRun = await selectResearchIdentityCandidate(run.id, selectedIdentityCandidateId);
      setRun(nextRun);
      if (nextRun.stage === "Failed") {
        setError(nextRun.error || "RAVEN could not continue targeted discovery.");
        setView("failed");
        return;
      }

      const discoveredCandidates = await getResearchCandidates(nextRun.id);
      setCandidates(discoveredCandidates.map((candidate) => ({ ...candidate, selected: candidate.selected || candidate.recommended })));
      setSources([]);
      setView("reviewingSources");
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "RAVEN could not continue targeted discovery."));
      setView("failed");
    } finally {
      setLoading(false);
    }
  }

  async function handleContinueWithoutGrounding() {
    if (!run) return;
    setError(null);
    setSelectionError(null);
    setLoading(true);
    try {
      const discoveredCandidates = await getResearchCandidates(run.id);
      setCandidates(discoveredCandidates.map((candidate) => ({ ...candidate, selected: candidate.selected || candidate.recommended })));
      setIdentityCandidates([]);
      setSelectedIdentityCandidateId(null);
      setView("reviewingSources");
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "RAVEN could not load the discovered sources."));
      setView("failed");
    } finally {
      setLoading(false);
    }
  }

  async function handleAlternateIdentitySearch() {
    const requestedName = alternateIdentityHint.trim();
    if (!run || !company || !requestedName || isPaused) return;

    setLoading(true);
    setError(null);
    setSelectionError(null);
    try {
      await cancelResearchRun(run.id);
      clearCurrentResearch(run.id);
      const focusedHint = [form.researchHint?.trim(), `Focus the research on the organization or subsidiary named "${requestedName}".`]
        .filter(Boolean)
        .join(" ");
      setAlternateIdentityHint("");
      await discoverForCompany(company, false, focusedHint);
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "RAVEN could not search for that related organization."));
      setView("resolvingIdentity");
    } finally {
      setLoading(false);
    }
  }

  function togglePause() {
    if (!run || !canPauseResearchStage(run.stage)) return;
    const next = setCurrentResearchPaused(run.id, !isPaused);
    setIsPaused(next?.paused === true);
  }

  async function cancelCurrentResearch() {
    if (!run) return;
    setLoading(true);
    try {
      const cancelled = await cancelResearchRun(run.id);
      setRun(cancelled);
      clearCurrentResearch(run.id);
      setIsPaused(false);
      setView("cancelled");
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "RAVEN could not cancel this research run."));
    } finally {
      setLoading(false);
    }
  }

  function focusIdentityForm() {
    clearCurrentResearch();
    setView("identify");
    setError(null);
    setSelectionError(null);
    setMatches([]);
    setCompany(null);
    setRun(null);
    setIdentityCandidates([]);
    setSelectedIdentityCandidateId(null);
    setCandidates([]);
    setSources([]);
    setProfileCandidate(null);
    setProfileWarnings([]);
    setIsPaused(false);
    window.setTimeout(() => identityHeadingRef.current?.focus(), 0);
  }

  function updateCandidateSelection(id: string, selected: boolean) {
    setSelectionError(null);
    setCandidates((current) => current.map((candidate) => candidate.id === id ? { ...candidate, selected } : candidate));
  }

  function toggleStrengtheningTarget(target: ResearchTarget) {
    setStrengtheningTargets((current) => current.includes(target)
      ? current.filter((item) => item !== target)
      : [...current, target]);
  }

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

  function resetAfterFailure() {
    setView("identify");
    setError(null);
    setMatches([]);
    setCompany(null);
    setRun(null);
    setCandidates([]);
    setSources([]);
    setProfileCandidate(null);
  }

  return {
    form,
    groundingOverride,
    setGroundingOverride,
    defaultGroundingMode,
    view,
    setView,
    company,
    run,
    matches,
    preflightResponse,
    selectedPreflightEntityId,
    setSelectedPreflightEntityId,
    identityCandidates,
    selectedIdentityCandidateId,
    setSelectedIdentityCandidateId,
    alternateIdentityHint,
    setAlternateIdentityHint,
    candidates,
    sources,
    coverage,
    strengtheningTargets,
    toggleStrengtheningTarget,
    loading,
    error,
    selectionError,
    profileCandidate,
    profileWarnings,
    isPaused,
    identityHeadingRef,
    selectedCount,
    coverageGaps,
    activityCounters,
    canPause,
    researchStatusDetail,
    updateField,
    handleIdentitySubmit,
    handlePreflightSelection,
    requestPreflightClarification,
    retryPreflightIdentity,
    researchExactName,
    handleResearchExisting,
    createAndDiscover,
    handleSelectIdentityCandidate,
    handleContinueWithoutGrounding,
    handleAlternateIdentitySearch,
    togglePause,
    cancelCurrentResearch,
    focusIdentityForm,
    updateCandidateSelection,
    handleAcquire,
    handleStrengthenDossier,
    handleGenerateProfile,
    handleConfirmProfile,
    resetAfterFailure,
  };
}
