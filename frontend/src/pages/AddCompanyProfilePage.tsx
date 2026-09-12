import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Pause, Play, Sparkle, StopCircle } from "@phosphor-icons/react";
import { Button } from "../components/Button";
import { Panel } from "../components/Panel";
import { TextInput } from "../components/TextInput";
import {
  CandidateSourceCard,
  EvidenceCard,
  ResearchActivity,
  type CandidateSource,
  type EvidenceRecord,
  type ResearchStage as ActivityStage,
} from "../components/sources";
import { getApiErrorMessage } from "../api/client";
import { confirmCompanyProfile, generateCompanyProfile, getCompanyProfileCandidate } from "../api/profiles";
import { createCompany, findCompanyMatches, getCompany } from "../api/companies";
import {
  acquireResearchCandidates,
  cancelResearchRun,
  getResearchRun,
  startBackgroundResearch,
  getResearchCandidates,
  getResearchIdentityCandidates,
  getResearchSources,
  selectResearchIdentityCandidate,
} from "../api/research";
import { getResearchRunCoverage, type EvidenceCoverageResponse, type ResearchTarget } from "../api/coverage";
import { getResearchSettings } from "../api/settings";
import type { Company, CompanyMatchResponse, CreateCompanyRequest } from "../types/company";
import type { GroundingMode, ResearchCandidate, ResearchIdentityCandidate, ResearchRun, SourceDocument } from "../types/research";
import type { CompanyProfileCandidate } from "../types/profile";
import styles from "./research-workspace.module.css";
import { canPauseResearchStage, researchProgressLabel } from "../utils/researchProgress";
import { clearCurrentResearch, readCurrentResearch, rememberCurrentResearch, setCurrentResearchPaused } from "../utils/researchSession";

type IdentityForm = {
  name: string;
  legalName: string;
  website: string;
  country: string;
  registrationNumber: string;
  headquarters: string;
  researchHint: string;
};

type WorkspaceView = "identify" | "matching" | "discovering" | "resolvingIdentity" | "reviewingSources" | "acquiring" | "reviewingEvidence" | "generatingProfile" | "reviewingProfile" | "completed" | "failed" | "cancelled";
type GroundingOverride = "default" | GroundingMode;

const initialForm: IdentityForm = {
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

function activityStage(view: WorkspaceView): ActivityStage {
  switch (view) {
    case "discovering":
      return "discovering";
    case "resolvingIdentity":
      return "resolvingIdentity";
    case "reviewingSources":
      return "awaitingSourceSelection";
    case "acquiring":
      return "acquiring";
    case "reviewingEvidence":
      return "evidenceReady";
    case "generatingProfile":
      return "generatingProfile";
    case "reviewingProfile":
      return "awaitingProfileConfirmation";
    case "completed":
      return "completed";
    case "cancelled":
      return "cancelled";
    case "failed":
      return "failed";
    case "identify":
    case "matching":
      return "identifying";
  }
}

function entityTypeLabel(entityType: ResearchIdentityCandidate["entityType"]) {
  switch (entityType) {
    case "ParentGroup":
      return "Parent group";
    case "Subsidiary":
      return "Subsidiary";
    case "Affiliate":
      return "Affiliate";
    case "Brand":
      return "Brand";
    case "Company":
      return "Company";
    default:
      return "Organization";
  }
}

function confidenceLabel(confidence: ResearchIdentityCandidate["confidence"]) {
  return `${confidence.toLowerCase()} confidence`;
}

function targetLabel(target: ResearchTarget) {
  return target.replace(/([a-z])([A-Z])/g, "$1 $2").replace("Products Services", "Products / services");
}

function relationshipLabel(relationship?: ResearchCandidate["entityRelationship"]) {
  switch (relationship) {
    case "SameEntity":
      return "Matches selected entity";
    case "Parent":
      return "Related parent group";
    case "Subsidiary":
      return "Related subsidiary";
    case "Affiliate":
      return "Related affiliate";
    case "DifferentEntity":
      return "Likely different entity";
    case "Uncertain":
      return "Entity relationship uncertain";
    default:
      return undefined;
  }
}

function semanticSummary(candidate: ResearchCandidate) {
  const relationship = relationshipLabel(candidate.entityRelationship);
  const relevance = candidate.semanticRelevance ? `${candidate.semanticRelevance.toLowerCase()} relevance` : undefined;
  const purposes = candidate.semanticPurposes?.slice(0, 3).join(", ");
  const rationale = candidate.semanticRationale?.trim();
  return [relationship, relevance, purposes ? `Useful for ${purposes}` : undefined, rationale].filter(Boolean).join(" · ");
}

function matchStrengthLabel(strength: CompanyMatchResponse["matchStrength"]) {
  switch (strength) {
    case "Exact":
      return "Exact identity match";
    case "VeryStrong":
      return "Very strong match";
    case "Strong":
      return "Strong match";
    default:
      return "Possible match";
  }
}

function formatDate(value?: string | null) {
  if (!value) return "Not researched yet";
  const timestamp = Date.parse(value);
  if (Number.isNaN(timestamp)) return "Not researched yet";
  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(timestamp);
}

function candidateStatus(candidate: ResearchCandidate): CandidateSource["acquisitionStatus"] {
  switch (candidate.acquisitionStatus) {
    case "Acquiring":
      return "pending";
    case "Acquired":
      return "acquired";
    case "Failed":
    case "Unavailable":
      return "failed";
    case "DuplicateSkipped":
      return "duplicate";
    default:
      return "idle";
  }
}

function toCandidateSource(candidate: ResearchCandidate): CandidateSource {
  const aiSummary = semanticSummary(candidate);
  const displaySnippet = aiSummary
    ? [candidate.snippet, `RAVEN assessment: ${aiSummary}`].filter(Boolean).join(" · ")
    : candidate.snippet;
  const recommendationReasons = aiSummary && candidate.recommended
    ? [...candidate.recommendationReasons, `AI: ${aiSummary}`]
    : candidate.recommendationReasons;

  return {
    id: candidate.id,
    url: candidate.url,
    title: candidate.title || candidate.domain || "Untitled source",
    domain: candidate.domain,
    snippet: displaySnippet,
    kind: candidate.sourceKind,
    iconUrl: candidate.iconUrl,
    recommended: candidate.recommended,
    recommendationReasons,
    selected: candidate.selected,
    acquisitionStatus: candidateStatus(candidate),
    acquisitionMessage: candidate.acquisitionError,
  };
}

function toEvidenceRecord(source: SourceDocument): EvidenceRecord {
  return {
    id: source.id,
    url: source.url,
    title: source.title || source.sourceDomain || "Untitled source",
    domain: source.sourceDomain,
    kind: source.sourceKind,
    iconUrl: source.iconUrl,
    preview: source.contentPreview,
    retrievedAt: source.retrievedAt,
    crawlerProvider: source.crawlerProvider,
    status: "acquired",
  };
}

export function AddCompanyProfilePage() {
  const [searchParams] = useSearchParams();
  const [form, setForm] = useState<IdentityForm>(initialForm);
  const [groundingOverride, setGroundingOverride] = useState<GroundingOverride>("default");
  const [defaultGroundingMode, setDefaultGroundingMode] = useState<GroundingMode>("Auto");
  const [view, setView] = useState<WorkspaceView>("identify");
  const [company, setCompany] = useState<Company | null>(null);
  const [run, setRun] = useState<ResearchRun | null>(null);
  const [matches, setMatches] = useState<CompanyMatchResponse[]>([]);
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
    .map((item) => item.target) ?? [], [coverage]);
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

  async function discoverForCompany(nextCompany: Company, useAcceptedProfileIdentity = false, researchHintOverride?: string) {
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
          // Grounding is an assistive step. A missing or unavailable resolver must not block deterministic discovery.
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
      await discoverForCompany(nextCompany);
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
    setView("matching");

    try {
      const identity = companyRequest(form);
      const foundMatches = await findCompanyMatches({
        name: identity.name,
        legalName: identity.legalName,
        website: identity.website,
        country: identity.country,
        registrationNumber: identity.registrationNumber,
      });

      if (foundMatches.length > 0) {
        setMatches(foundMatches);
        setLoading(false);
        return;
      }

      setLoading(false);
      await createAndDiscover();
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "RAVEN could not check this company identity."));
      setView("failed");
      setLoading(false);
    }
  }

  async function handleResearchExisting(existingCompany: Company) {
    // Preserve the user's disambiguating hints when reusing an existing
    // sparse record. The stored Company identity is authoritative for the
    // record, but a newly entered country/legal hint still matters for the
    // first-pass family search (for example, "FPT" + "Vietnam").
    const identityHint = [
      form.country.trim() ? `Country: ${form.country.trim()}` : "",
      form.legalName.trim() ? `Legal name: ${form.legalName.trim()}` : "",
      form.researchHint.trim(),
    ].filter(Boolean).join("; ");
    await discoverForCompany(existingCompany, false, identityHint || undefined);
  }

  useEffect(() => {
    const requestedRunId = searchParams.get("researchRun") || (!searchParams.get("refreshCompanyId") ? readCurrentResearch()?.runId : null);
    if (!requestedRunId || restoredRunRef.current === requestedRunId) return;

    restoredRunRef.current = requestedRunId;
    void restoreResearchRun(requestedRunId);
  // The run identifier is the intentional restore key; the restore function only reads current server state.
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
      // The current run is waiting for a choice, so it is safe to replace it.
      // Keeping only one active identity-resolution run avoids stale choices
      // competing with the new target in sessionStorage and the global strip.
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
        .map((item) => item.target) ?? []);
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

  const identityPanel = view === "identify" || view === "matching" ? (
    <Panel title="Company identity" eyebrow="STEP 01 · IDENTIFY" className={styles.identityPanel}>
      <form onSubmit={handleIdentitySubmit}>
        <fieldset className={styles.fieldset} disabled={loading && view === "matching"}>
          <legend className={styles.visuallyHidden}>Company identity details</legend>
          <div className={styles.formGrid}>
            <TextInput label="Company name" id="research-name" name="name" autoComplete="organization" value={form.name} onChange={(event) => updateField("name", event.target.value)} placeholder="e.g. FPT Software" required />
            <TextInput label="Legal name" id="research-legal-name" name="legalName" autoComplete="organization" value={form.legalName} onChange={(event) => updateField("legalName", event.target.value)} placeholder="Optional registered name" />
            <TextInput label="Website" id="research-website" name="website" type="url" autoComplete="url" value={form.website} onChange={(event) => updateField("website", event.target.value)} placeholder="https://example.com" />
            <TextInput label="Country" id="research-country" name="country" autoComplete="country-name" value={form.country} onChange={(event) => updateField("country", event.target.value)} placeholder="e.g. Vietnam" />
            <TextInput label="Registration / tax ID" id="research-registration" name="registrationNumber" autoComplete="off" value={form.registrationNumber} onChange={(event) => updateField("registrationNumber", event.target.value)} placeholder="Optional identifier" />
            <TextInput label="Headquarters / address" id="research-headquarters" name="headquarters" autoComplete="street-address" value={form.headquarters} onChange={(event) => updateField("headquarters", event.target.value)} placeholder="Optional research hint" />
          </div>
          <div className="field">
            <label htmlFor="research-hint">Research hint</label>
            <textarea className={styles.textarea} id="research-hint" name="researchHint" value={form.researchHint} onChange={(event) => updateField("researchHint", event.target.value)} placeholder="What should RAVEN pay attention to?" maxLength={500} rows={3} />
            <p className="field__hint">Hints help discovery; they are not accepted profile facts until public evidence supports them.</p>
          </div>
          <fieldset className={styles.groundingFieldset} aria-describedby="grounding-help">
            <legend className={styles.visuallyHidden}>AI-assisted grounding</legend>
            <div className={styles.groundingHeading}>
              <span className={styles.groundingIcon} aria-hidden="true"><Sparkle size={18} weight="duotone" /></span>
              <div>
                <strong>AI-assisted grounding</strong>
                <span className={styles.groundingDefault}>{groundingOverride === "default" ? `Workspace default · ${defaultGroundingMode}` : groundingOverride === "Always" ? "One-run override · On" : "One-run override · Off"}</span>
              </div>
            </div>
            <p className={styles.groundingHelp} id="grounding-help">Resolve ambiguous company names and improve source recommendations using public search evidence.</p>
            <div className={styles.groundingOptions}>
              <label className={styles.groundingOption}>
                <input name="groundingOverride" type="radio" value="default" checked={groundingOverride === "default"} onChange={() => setGroundingOverride("default")} />
                <span><strong>Use default</strong><small>Auto when needed</small></span>
              </label>
              <label className={styles.groundingOption}>
                <input name="groundingOverride" type="radio" value="Always" checked={groundingOverride === "Always"} onChange={() => setGroundingOverride("Always")} />
                <span><strong>On</strong><small>Always ground this run</small></span>
              </label>
              <label className={styles.groundingOption}>
                <input name="groundingOverride" type="radio" value="Off" checked={groundingOverride === "Off"} onChange={() => setGroundingOverride("Off")} />
                <span><strong>Off</strong><small>Use deterministic research</small></span>
              </label>
            </div>
          </fieldset>
        </fieldset>
        {error && view === "matching" ? <p className="form-error" role="alert">{error}</p> : null}
        <div className="form-actions">
          <Button type="submit" loading={loading}>{view === "matching" ? "Checking for existing companies" : "Research public sources"}</Button>
          <Link className="button button--quiet" to="/companies">Cancel</Link>
        </div>
      </form>
    </Panel>
  ) : null;

  const matchPanel = view === "matching" && matches.length > 0 ? (
    <Panel title="Existing company found" eyebrow="STEP 02 · REVIEW IDENTITY" className={styles.matchPanel}>
      <p className={styles.panelIntro}>RAVEN found likely existing records. Reuse one to preserve research history, or create a separate company intentionally.</p>
      <div className={styles.matchList}>
        {matches.map((match) => (
          <article className={styles.matchCard} key={match.company.id}>
            <div>
              <p className="eyebrow">{matchStrengthLabel(match.matchStrength)}</p>
              <h3>{match.company.name}</h3>
              {match.company.legalName && <p>{match.company.legalName}</p>}
              <p className={styles.matchReason}>{match.matchReason}</p>
              <dl className={styles.matchMeta}>
                {match.company.website && <div><dt>Website</dt><dd>{match.company.website}</dd></div>}
                {match.company.country && <div><dt>Country</dt><dd>{match.company.country}</dd></div>}
                <div><dt>Last researched</dt><dd>{formatDate(match.company.lastResearchedAt)}</dd></div>
              </dl>
            </div>
            <Button type="button" onClick={() => void handleResearchExisting(match.company)} loading={loading}>Research existing company</Button>
          </article>
        ))}
      </div>
      <div className={styles.overrideBox}>
        <div><strong>Different company?</strong><p>Create a separate record while keeping this match available for reference.</p></div>
        <Button type="button" tone="secondary" onClick={() => void createAndDiscover()} loading={loading}>Create separate anyway</Button>
      </div>
    </Panel>
  ) : null;

  const identityResolutionPanel = view === "resolvingIdentity" ? (
    <Panel title="Resolve research target" eyebrow="STEP 03 · AI GROUNDING" className={styles.identityResolutionPanel}>
      <p className={styles.panelIntro}>RAVEN found {identityCandidates.length || "several"} public organizations related to <strong>{form.name || "this search"}</strong>. Parent groups, subsidiaries, affiliates, and same-name companies are shown together so you can choose the exact target before deeper discovery.</p>
      <div className={styles.identityCandidateList} role="radiogroup" aria-label="Possible research targets">
        {identityCandidates.map((candidate) => {
          const candidateLabelId = `identity-candidate-${candidate.id}`;
          return (
            <label className={`${styles.identityCandidateCard} ${selectedIdentityCandidateId === candidate.id ? styles.identityCandidateCardSelected : ""}`} htmlFor={candidateLabelId} key={candidate.id}>
              <input
                id={candidateLabelId}
                name="researchIdentityCandidate"
                type="radio"
                value={candidate.id}
                checked={selectedIdentityCandidateId === candidate.id}
                onChange={() => { setSelectedIdentityCandidateId(candidate.id); setSelectionError(null); }}
              />
              <span className={styles.identityCandidateCopy}>
                <span className={styles.identityCandidateTopline}>
                  <strong>{candidate.displayName}</strong>
                  {candidate.recommended && <span className={styles.identityRecommended}>Recommended</span>}
                </span>
                <span className={styles.identityCandidateMeta}>{entityTypeLabel(candidate.entityType)} · {confidenceLabel(candidate.confidence)}</span>
                <span className={styles.identityCandidateDetails}>
                  {[candidate.legalName, candidate.country, candidate.officialDomain || candidate.website].filter(Boolean).join(" · ") || "Public identity details are still limited."}
                </span>
                {candidate.relationshipHint && <span className={styles.identityCandidateRelationship}>{candidate.relationshipHint}</span>}
                {candidate.rationale && <span className={styles.identityCandidateRationale}>{candidate.rationale}</span>}
              </span>
            </label>
          );
        })}
      </div>
      {selectionError ? <p className="form-error" role="alert">{selectionError}</p> : null}
      <div className={styles.alternateIdentityBox}>
        <div>
          <strong>Can’t find the intended organization?</strong>
          <p>Search again with a subsidiary, brand, legal name, or another identifying phrase.</p>
        </div>
        <div className={styles.alternateIdentityForm}>
          <TextInput
            id="alternate-identity"
            name="alternateIdentity"
            label="Another organization name"
            value={alternateIdentityHint}
            onChange={(event) => setAlternateIdentityHint(event.target.value)}
            placeholder="e.g. FPT IS or FPT Telecom"
            disabled={loading || isPaused}
          />
          <Button type="button" tone="secondary" onClick={() => void handleAlternateIdentitySearch()} loading={loading} disabled={!alternateIdentityHint.trim() || isPaused}>Search this name</Button>
        </div>
      </div>
      <div className="form-actions">
        <Button type="button" onClick={() => void handleSelectIdentityCandidate()} loading={loading} disabled={identityCandidates.length === 0 || isPaused}>Research selected company</Button>
        <Button type="button" tone="secondary" onClick={focusIdentityForm} disabled={loading}>Back to edit</Button>
        <Button type="button" tone="quiet" onClick={() => void handleContinueWithoutGrounding()} disabled={loading}>Continue without grounding</Button>
      </div>
    </Panel>
  ) : null;

  const candidatePanel = view === "reviewingSources" || view === "acquiring" ? (
    <Panel title="Review source candidates" eyebrow="STEP 03 · SOURCE SELECTION" className={styles.sourcesPanel}>
      <div className={styles.sectionSummary}>
        <div><strong>{candidates.length} unique source{candidates.length === 1 ? "" : "s"}</strong><p>{run?.recommendedCandidates ?? 0} recommended by RAVEN · {selectedCount} selected</p></div>
        <span className={styles.selectionPill}>{selectedCount} selected</span>
      </div>
      {candidates.length > 0 ? (
        <div className={styles.candidateGrid}>
          {candidates.map((candidate) => (
            <CandidateSourceCard key={candidate.id} candidate={toCandidateSource(candidate)} disabled={loading || isPaused} onSelectionChange={(selected) => updateCandidateSelection(candidate.id, selected)} />
          ))}
        </div>
      ) : (
        <p className="empty-state">No public source candidates were found. Try another identity or research hint.</p>
      )}
      {selectionError ? <p className="form-error" role="alert">{selectionError}</p> : null}
      <div className="form-actions">
      <Button type="button" onClick={() => void handleAcquire()} loading={view === "acquiring"} disabled={candidates.length === 0 || isPaused}>Acquire {selectedCount} selected source{selectedCount === 1 ? "" : "s"}</Button>
      </div>
    </Panel>
  ) : null;

  const evidencePanel = view === "reviewingEvidence" ? (
    <Panel title="Evidence ready" eyebrow="STEP 04 · REVIEW EVIDENCE" className={styles.evidencePanel}>
      <div className={styles.sectionSummary}>
        <div><strong>{sources.length} acquired source{sources.length === 1 ? "" : "s"}</strong><p>Review the public pages RAVEN preserved before generating a Company Profile.</p></div>
        <span className={styles.successPill}>{run?.documentsAdded ?? sources.length} documents added</span>
      </div>
      {coverage ? <section className={styles.coverageSummary} aria-label="Research coverage"><h3>Research coverage</h3><ul>{coverage.items.map((item) => <li key={item.target}><span>{targetLabel(item.target)}</span><strong>{item.level}</strong></li>)}</ul>{coverage.budgetExhausted ? <p>Research budget reached. Remaining gaps stay unknown.</p> : null}</section> : null}
      {sources.length > 0 ? <div className={styles.evidenceGrid}>{sources.map((source) => <EvidenceCard key={source.id} evidence={toEvidenceRecord(source)} />)}</div> : <p className="empty-state">No source documents were acquired. The selected sources may have been unavailable.</p>}
      {candidates.some((candidate) => candidate.acquisitionStatus === "Failed" || candidate.acquisitionStatus === "Unavailable" || candidate.acquisitionStatus === "DuplicateSkipped") ? (
        <div className={styles.acquisitionIssues} role="status">
          <h3>Sources not added as evidence</h3>
          <ul>
            {candidates.filter((candidate) => candidate.acquisitionStatus === "Failed" || candidate.acquisitionStatus === "Unavailable" || candidate.acquisitionStatus === "DuplicateSkipped").map((candidate) => (
              <li key={candidate.id}><strong>{candidate.title || candidate.domain}</strong> — {candidate.acquisitionStatus === "DuplicateSkipped" ? "duplicate content skipped" : candidate.acquisitionError || "source unavailable"}</li>
            ))}
          </ul>
        </div>
      ) : null}
      {coverageGaps.length > 0 ? <fieldset className={styles.coverageTargets}><legend>Areas to strengthen</legend>{coverageGaps.map((target) => <label key={target}><input type="checkbox" checked={strengtheningTargets.includes(target)} onChange={() => setStrengtheningTargets((current) => current.includes(target) ? current.filter((item) => item !== target) : [...current, target])} /> {targetLabel(target)}</label>)}</fieldset> : null}
      <div className={styles.profileNextStep}>
        <div><p className="eyebrow">NEXT · AI PROFILE</p><h3>Generate a grounded Company Profile</h3><p>Gemini profile generation will use these preserved documents and attach evidence references.</p></div>
        <div className="form-actions"><Button type="button" onClick={() => void handleStrengthenDossier()} loading={loading} disabled={strengtheningTargets.length === 0 || isPaused}>Strengthen dossier</Button><Button type="button" onClick={() => void handleGenerateProfile()} loading={loading} tone="secondary" disabled={isPaused}>Generate profile now</Button></div>
      </div>
    </Panel>
  ) : null;

  const profilePanel = view === "reviewingProfile" && profileCandidate ? (
    <Panel title="Company Profile preview" eyebrow="STEP 05 · REVIEW PROFILE" className={styles.evidencePanel}>
      <p className="page-intro">This is a generated candidate, not accepted company truth. Confirm only after reviewing its evidence.</p>
      <dl className="definition-list"><div><dt>Summary</dt><dd>{profileCandidate.summary || "Not verified"}</dd></div><div><dt>Industry</dt><dd>{profileCandidate.primaryIndustry || "Not verified"}</dd></div><div><dt>Scale</dt><dd>{profileCandidate.employeeCountRange || profileCandidate.companySize || "Not verified"}</dd></div><div><dt>Evidence groups</dt><dd>{profileCandidate.evidence.length}</dd></div></dl>
      {profileCandidate.productsServices.length ? <section><h3>Products & services</h3><ul>{profileCandidate.productsServices.map((item) => <li key={`${item.name}-${item.type}`}>{item.name}{item.description ? ` — ${item.description}` : ""}</li>)}</ul></section> : null}
      {profileWarnings.length ? <div className={styles.acquisitionIssues} role="status"><h3>Validation notes</h3><ul>{profileWarnings.map((warning) => <li key={warning}>{warning}</li>)}</ul></div> : null}
      <div className="form-actions"><Button type="button" onClick={() => void handleConfirmProfile()} loading={loading}>Confirm Profile</Button><Button type="button" tone="secondary" onClick={() => setView("reviewingEvidence")}>Back to Evidence</Button></div>
    </Panel>
  ) : null;

  return (
    <div className={`page-stack add-profile-page ${styles.page}`}>
      <div className="page-title-row">
        <div>
          <p className="eyebrow">PUBLIC-SOURCE INTELLIGENCE</p>
          <h1 ref={identityHeadingRef} tabIndex={-1}>Research a company</h1>
          <p className="page-intro">Build a defensible company dossier from public evidence. RAVEN separates identity hints, source review, and acquired evidence.</p>
        </div>
      </div>

      <div className={styles.workspace}>
        <main className={styles.primaryColumn}>
          {identityPanel}
          {matchPanel}
          {identityResolutionPanel}
          {candidatePanel}
          {evidencePanel}
          {view === "generatingProfile" ? <Panel title="Building Company Profile" eyebrow="STEP 05 · GEMINI"><p aria-live="polite">Gemini is normalizing only acquired evidence. RAVEN will show a candidate for confirmation when it returns.</p></Panel> : null}
          {profilePanel}
          {view === "completed" && company ? <Panel title="Company Profile confirmed" eyebrow="RESEARCH COMPLETE"><p>Your accepted dossier is now versioned and traceable to selected evidence.</p><Link className="button" to={`/companies/${company.id}`}>Open Company workspace</Link></Panel> : null}
          {view === "failed" && error ? <Panel title="Research needs attention" eyebrow="RESEARCH FAILED" className={styles.failurePanel}><p className="form-error" role="alert">{error}</p><Button type="button" tone="secondary" onClick={() => { setView("identify"); setError(null); setMatches([]); setCompany(null); setRun(null); setCandidates([]); setSources([]); setProfileCandidate(null); }}>Start over</Button></Panel> : null}
        </main>
        <aside className={styles.contextColumn}>
          <ResearchActivity
            stage={activityStage(view)}
            items={view === "resolvingIdentity"
              ? [
                  { id: "grounding-complete", label: "Ground company identity", status: "completed", detail: `RAVEN found ${identityCandidates.length} possible research targets.` },
                  { id: "identity-review", label: "Choose the research target", status: "waiting", detail: "Select the intended organization to rebuild targeted source queries." },
                ]
              : view === "matching" && matches.length > 0
                ? [{ id: "duplicate-review", label: "Review existing company match", status: "waiting", detail: "Choose an existing record or create a separate company." }]
                : undefined}
            counters={activityCounters}
            failureMessage={view === "failed" ? error : undefined}
            statusDetail={run ? researchStatusDetail : undefined}
          />
          {run && !["identify", "matching", "completed", "cancelled"].includes(view) ? <div className={styles.researchControls} data-paused={isPaused ? "true" : undefined}>
            <div className={styles.researchControlsCopy}>
              <strong>{isPaused ? "Research paused" : "Research controls"}</strong>
              <span>{canPause ? "Pause at this checkpoint and resume later in this browser tab." : "RAVEN is in a live provider operation. Pause becomes available when this step is safe."}</span>
            </div>
            <div className={styles.researchControlsActions}>
              <Button type="button" tone="secondary" onClick={togglePause} disabled={loading || (!canPause && !isPaused)}>{isPaused ? <><Play size={16} weight="fill" /> Resume research</> : <><Pause size={16} weight="fill" /> Pause research</>}</Button>
              <Button type="button" tone="quiet" onClick={() => void cancelCurrentResearch()} disabled={loading}><StopCircle size={16} weight="bold" /> Cancel research</Button>
            </div>
          </div> : null}
          {company ? <div className={styles.compactContext} aria-label="Company context"><span className={styles.compactContextIcon} aria-hidden="true">{company.name.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]).join("").toUpperCase()}</span><span className={styles.compactContextCopy}><small>Research target</small><strong>{company.name}</strong><span>{[company.country, company.legalName || company.website].filter(Boolean).join(" · ") || "Identity details are still being verified"}</span></span></div> : <Panel title="What RAVEN will do" eyebrow="RESEARCH WORKFLOW" className={styles.contextPanel}><ol className={styles.workflowList}><li><strong>Identify</strong><span>Capture a stable company identity and optional hints.</span></li><li><strong>Discover</strong><span>Find and classify public source candidates.</span></li><li><strong>Acquire</strong><span>Let you choose which pages become evidence.</span></li><li><strong>Profile</strong><span>Generate only from acquired, traceable evidence.</span></li></ol></Panel>}
        </aside>
      </div>
    </div>
  );
}
