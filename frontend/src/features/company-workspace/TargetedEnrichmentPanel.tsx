import { useEffect, useMemo, useRef, useState } from "react";
import { ArrowSquareOut, Sparkle } from "@phosphor-icons/react";
import { getApiErrorMessage } from "../../api/client";
import { getResearchCandidates, getResearchSources, acquireResearchCandidates } from "../../api/research";
import { getManagedResearchJobs, startManagedResearch } from "../../api/managedResearch";
import {
  confirmProfilePatch,
  generateProfilePatch,
  startTargetedResearch,
  type ProfilePatchCandidate,
} from "../../api/profiles";
import type { ResearchTarget } from "../../api/coverage";
import type { ResearchCandidate, ResearchRun, SourceDocument } from "../../types/research";
import type { CompanyProfileVersion } from "../../types/profile";
import { CandidateSourceCard, EvidenceCard, type CandidateSource, type EvidenceRecord } from "../../components/sources";
import styles from "./company-workspace.module.css";
import type { DossierCompany, DossierProfile } from "./dossierTypes";
import { upsertResearchActivity } from "../../utils/researchActivity";
import { hasUsableAcceptedProfile } from "../../utils/profileReadiness";

const targetOrder: ResearchTarget[] = [
  "LegalIdentity",
  "TaxRegistration",
  "FoundedHistory",
  "Industry",
  "EmployeeScale",
  "ProductsServices",
  "Markets",
  "Leadership",
  "Locations",
];

const targetLabels: Record<ResearchTarget, string> = {
  LegalIdentity: "Legal identity",
  TaxRegistration: "Tax registration",
  FoundedHistory: "Founded / history",
  Industry: "Industry",
  EmployeeScale: "Company scale",
  ProductsServices: "Products & services",
  Markets: "Markets & customer segments",
  Leadership: "Leadership",
  Locations: "Locations",
};

type EnrichmentPhase = "choose" | "discovering" | "reviewingSources" | "acquiring" | "reviewingEvidence" | "generatingPatch" | "reviewingPatch" | "confirmed" | "failed";

export interface TargetedEnrichmentPanelProps {
  company: DossierCompany;
  profile?: DossierProfile | null;
  initialTargets: ResearchTarget[];
  initialMaterialArtifactId?: string | null;
  initialManagedResearchInvestigationId?: string | null;
  open: boolean;
  onClose: () => void;
  onConfirmed?: (profile: CompanyProfileVersion) => void;
  onOpenExternalResearch?: (targets: ResearchTarget[]) => void;
}

function toCandidateSource(candidate: ResearchCandidate): CandidateSource {
  const semantic = [
    candidate.entityRelationship ? `Entity: ${candidate.entityRelationship}` : undefined,
    candidate.semanticRelevance ? `${candidate.semanticRelevance} relevance` : undefined,
    candidate.semanticPurposes?.length ? `Covers ${candidate.semanticPurposes.slice(0, 3).join(" · ")}` : undefined,
    candidate.semanticRationale,
  ].filter(Boolean).join(" · ");
  return {
    id: candidate.id,
    url: candidate.url,
    title: candidate.title || candidate.domain || "Untitled source",
    domain: candidate.domain,
    snippet: [candidate.snippet, semantic].filter(Boolean).join(" · "),
    kind: candidate.sourceKind,
    iconUrl: candidate.iconUrl,
    recommended: candidate.recommended,
    recommendationReasons: candidate.recommendationReasons,
    selected: candidate.selected || candidate.recommended,
    acquisitionStatus: candidate.acquisitionStatus === "Acquired" ? "acquired" : candidate.acquisitionStatus === "Failed" || candidate.acquisitionStatus === "Unavailable" ? "failed" : candidate.acquisitionStatus === "DuplicateSkipped" ? "duplicate" : candidate.acquisitionStatus === "Acquiring" ? "pending" : "idle",
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

function fieldLabel(path: string) {
  const labels: Record<string, string> = {
    displayName: "Display name",
    legalName: "Legal name",
    website: "Official website",
    country: "Country",
    headquarters: "Headquarters",
    registrationNumberOrTaxId: "Registration / tax ID",
    foundedYear: "Founded year",
    primaryIndustry: "Primary industry",
    secondaryIndustries: "Secondary industries",
    companySize: "Company scale",
    employeeCount: "Employee count",
    employeeCountRange: "Employee range",
    productsServices: "Products & services",
    markets: "Markets",
    leadership: "Leadership",
    locations: "Locations",
  };
  return labels[path] || path;
}

function formatStructuredValue(value: unknown): string {
  if (value === null || value === undefined || value === "") return "";
  if (typeof value === "string" || typeof value === "number" || typeof value === "boolean") return String(value);
  if (Array.isArray(value)) return value.map(formatStructuredValue).filter(Boolean).join(", ");
  if (typeof value === "object") {
    const record = value as Record<string, unknown>;
    const preferred = record.name ?? record.fullName ?? record.title ?? record.role ?? record.value;
    if (preferred !== undefined) return formatStructuredValue(preferred);
    return Object.entries(record).map(([key, item]) => {
      const formatted = formatStructuredValue(item);
      return formatted ? key + ": " + formatted : "";
    }).filter(Boolean).join("; ");
  }
  return "";
}

function prettyValue(value?: string | null) {
  if (!value) return "Not verified";
  try {
    const parsed = JSON.parse(value) as unknown;
    const formatted = formatStructuredValue(parsed);
    return formatted || "Not verified";
  } catch {
    return value;
  }
}

export function TargetedEnrichmentPanel({ company, profile, initialTargets, initialMaterialArtifactId, initialManagedResearchInvestigationId, open, onClose, onConfirmed, onOpenExternalResearch }: TargetedEnrichmentPanelProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [targets, setTargets] = useState<ResearchTarget[]>(initialTargets);
  const [materialArtifactId, setMaterialArtifactId] = useState<string | null>(initialMaterialArtifactId ?? null);
  const [managedInvestigationId, setManagedInvestigationId] = useState<string | null>(initialManagedResearchInvestigationId ?? null);
  const [phase, setPhase] = useState<EnrichmentPhase>("choose");
  const [run, setRun] = useState<ResearchRun | null>(null);
  const [candidates, setCandidates] = useState<ResearchCandidate[]>([]);
  const [sources, setSources] = useState<SourceDocument[]>([]);
  const [patch, setPatch] = useState<ProfilePatchCandidate | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [overlappingManagedResearch, setOverlappingManagedResearch] = useState(false);
  const [showOverlapWarning, setShowOverlapWarning] = useState(false);

  useEffect(() => {
    setTargets(initialTargets);
    setMaterialArtifactId(initialMaterialArtifactId ?? null);
    setManagedInvestigationId(initialManagedResearchInvestigationId ?? null);
  }, [initialManagedResearchInvestigationId, initialMaterialArtifactId, initialTargets]);

  useEffect(() => {
    if (!open || phase !== "reviewingPatch") {
      setOverlappingManagedResearch(false);
      setShowOverlapWarning(false);
      return;
    }
    let active = true;
    void getManagedResearchJobs(company.id).then((jobs) => {
      if (!active) return;
      setOverlappingManagedResearch(jobs.some((job) => {
        if (job.status !== "Queued" && job.status !== "Researching") return false;
        const objective = job.objective.toLowerCase();
        return targets.some((target) => {
          const label = targetLabels[target].toLowerCase();
          const token = target === "EmployeeScale" ? "employee" : target === "ProductsServices" ? "product" : label.split(" ")[0];
          return objective.includes(label) || objective.includes(token);
        });
      }));
    }).catch(() => undefined);
    return () => { active = false; };
  }, [company.id, open, phase, targets]);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open) {
      if (typeof dialog.showModal === "function" && !dialog.open) dialog.showModal();
      else dialog.setAttribute("open", "");
    } else if (dialog.open) {
      dialog.close();
    }
  }, [open]);

  function reset() {
    setPhase("choose");
    setRun(null);
    setCandidates([]);
    setSources([]);
    setPatch(null);
    setLoading(false);
    setError(null);
    setOverlappingManagedResearch(false);
    setShowOverlapWarning(false);
    setTargets(initialTargets);
    setMaterialArtifactId(initialMaterialArtifactId ?? null);
    setManagedInvestigationId(initialManagedResearchInvestigationId ?? null);
  }

  function close() {
    if (dialogRef.current?.open) dialogRef.current.close();
    reset();
    onClose();
  }

  function toggleTarget(target: ResearchTarget) {
    setTargets((current) => current.includes(target) ? current.filter((item) => item !== target) : [...current, target]);
  }

  async function loadCandidates(researchRun: ResearchRun) {
    const discovered = await getResearchCandidates(researchRun.id);
    setCandidates(discovered.map((candidate) => ({ ...candidate, selected: candidate.selected || candidate.recommended })));
    setPhase("reviewingSources");
  }

  async function start() {
    if (!hasUsableAcceptedProfile(profile)) {
      setError("This saved profile is incomplete and cannot be improved yet. Create or repair the initial RAVEN profile first.");
      setPhase("failed");
      return;
    }
    if (targets.length === 0) {
      setError("Select at least one area to strengthen.");
      return;
    }
    setLoading(true);
    setError(null);
    setPhase("discovering");
    try {
      const nextRun = await startTargetedResearch(company.id, {
        targets,
        baseProfileVersionId: (profile as (DossierProfile & { id?: string }) | null | undefined)?.id,
        savedResearchArtifactId: materialArtifactId,
        managedResearchInvestigationId: managedInvestigationId,
      });
      setRun(nextRun);
      if (materialArtifactId || managedInvestigationId) {
        await generatePatch(nextRun.id);
      } else {
        await loadCandidates(nextRun);
      }
    } catch (reason) {
      setError(getApiErrorMessage(reason, "RAVEN could not start targeted research."));
      setPhase("failed");
    } finally {
      setLoading(false);
    }
  }

  async function launchDeepResearch() {
    if (targets.length === 0) return;
    setLoading(true);
    setError(null);
    const objective = `Investigate ${targets.map((target) => targetLabels[target]).join(", ")} for ${company.displayName}`;
    try {
      const job = await startManagedResearch(company.id, objective, { purpose: "ProfileImprovement" });
      upsertResearchActivity({
        id: `deep-${job.id}`,
        jobId: job.id,
        origin: "Deep",
        companyId: company.id,
        companyName: company.displayName,
        objective,
        detail: "Researching across sources",
        status: job.status === "Completed" ? "ready" : "running",
        locked: job.status === "Completed" && !hasUsableAcceptedProfile(profile),
        href: `/companies/${encodeURIComponent(company.id)}?tab=investigations&research=${encodeURIComponent(job.investigationId ?? job.id)}`,
        updatedAt: new Date().toISOString(),
      });
      onClose();
    } catch (reason) {
      setError(getApiErrorMessage(reason, "Deep Research could not be started."));
    } finally {
      setLoading(false);
    }
  }

  async function acquire() {
    if (!run) return;
    const selectedIds = candidates.filter((candidate) => candidate.selected).map((candidate) => candidate.id);
    if (selectedIds.length === 0) {
      setError("Select at least one candidate source before acquiring evidence.");
      return;
    }
    setLoading(true);
    setError(null);
    setPhase("acquiring");
    try {
      const nextRun = await acquireResearchCandidates(run.id, selectedIds);
      setRun(nextRun);
      const acquired = await getResearchSources(run.id);
      setSources(acquired);
      setPhase("reviewingEvidence");
    } catch (reason) {
      setError(getApiErrorMessage(reason, "RAVEN could not acquire the selected evidence."));
      setPhase("failed");
    } finally {
      setLoading(false);
    }
  }

  async function generatePatch(researchRunId = run?.id) {
    if (!researchRunId) return;
    setLoading(true);
    setError(null);
    setPhase("generatingPatch");
    try {
      setPatch(await generateProfilePatch(researchRunId));
      setPhase("reviewingPatch");
    } catch (reason) {
      setError(getApiErrorMessage(reason, "RAVEN could not generate a reviewable profile patch."));
      setPhase("failed");
    } finally {
      setLoading(false);
    }
  }

  async function confirmPatch() {
    if (overlappingManagedResearch && !showOverlapWarning) {
      setShowOverlapWarning(true);
      return;
    }
    if (!run || !patch || !patch.candidateId || patch.changes.length === 0) return;
    setLoading(true);
    setError(null);
    try {
      const nextProfile = await confirmProfilePatch(run.id, patch.candidateId);
      setPhase("confirmed");
      onConfirmed?.(nextProfile);
    } catch (reason) {
      setError(getApiErrorMessage(reason, "RAVEN could not confirm this profile update."));
      setPhase("failed");
    } finally {
      setLoading(false);
    }
  }

  function requestConfirmPatch() {
    if (overlappingManagedResearch) {
      setShowOverlapWarning(true);
      return;
    }
    void confirmPatch();
  }

  const selectedCandidates = useMemo(() => candidates.filter((candidate) => candidate.selected).length, [candidates]);
  const changedFields = useMemo(() => new Set((patch?.changes ?? []).map((change) => change.fieldPath)), [patch]);
  const unchangedFields = targetOrder.filter((target) => !patch?.allowedTargets.includes(target) && !["LegalIdentity", "TaxRegistration", "FoundedHistory", "Industry", "EmployeeScale", "ProductsServices", "Markets", "Leadership", "Locations"].some((item) => item === target && [...changedFields].some((field) => field.toLowerCase().includes(target.toLowerCase().replace("services", "")))));

  return (
    <dialog ref={dialogRef} className={styles.enrichmentDialog} aria-labelledby="targeted-enrichment-heading" onCancel={close}>
      <div className={styles.enrichmentDialogBody}>
        <header className={styles.enrichmentHeader}>
          <div><p className={styles.eyebrow}>{materialArtifactId || managedInvestigationId ? "INVESTIGATION PROFILE REVIEW" : "TARGETED RESEARCH"}</p><h2 id="targeted-enrichment-heading">{materialArtifactId || managedInvestigationId ? `Use findings for ${company.displayName}` : `Strengthen ${company.displayName}`}</h2><p className={styles.contextNote}>{materialArtifactId || managedInvestigationId ? "This uses the selected investigation material. No new search or source crawl will start." : "RAVEN will research only the areas you approve, then present a server-generated patch for review."}</p></div>
          <button className="button button--quiet" type="button" onClick={close} aria-label="Close targeted research">Close</button>
        </header>

        {phase === "choose" && (materialArtifactId || managedInvestigationId) ? <section className={`${styles.enrichmentSection} ${styles.materialReviewIntro}`} aria-labelledby="material-review-heading">
          <div className={styles.materialReviewLead}>
            <div>
              <p className={styles.eyebrow}>RAVEN REVIEW</p>
              <h3 id="material-review-heading">Review this investigation for the profile</h3>
              <p className={styles.contextNote}>RAVEN will compare the supplied findings with the accepted profile and prepare a proposed update. It will not search, crawl, or re-verify the investigation links.</p>
            </div>
            <span className={styles.materialReviewBadge}>Human confirmation required</span>
          </div>
          <div className={styles.materialReviewSteps} aria-label="Profile improvement steps">
            <span><strong>1</strong> AI review of findings</span>
            <span><strong>2</strong> You review the proposed changes</span>
            <span><strong>3</strong> Confirm a new profile version</span>
          </div>
          <div className={styles.materialReviewNotice}>
            <strong>What happens next</strong>
            <p>The proposal will show each current value, suggested value, and the material behind it. Nothing changes until you confirm.</p>
          </div>
          <div className="form-actions">
            <button className="button" type="button" onClick={() => void start()} disabled={loading}>{loading ? "Reviewing findings…" : "Review findings"}</button>
            <button className="button button--secondary" type="button" onClick={close}>Cancel</button>
          </div>
        </section> : null}

        {phase === "choose" && !materialArtifactId && !managedInvestigationId && <section className={styles.enrichmentSection} aria-labelledby="targeted-areas-heading">
          <h3 id="targeted-areas-heading">What should RAVEN strengthen?</h3>
          <p className={styles.contextNote}>Choose one or more missing or weak areas. Unselected accepted fields cannot be changed by this run.</p>
          <div className={styles.targetGrid}>
            {targetOrder.map((target) => <label className={styles.targetOption} key={target}><input type="checkbox" checked={targets.includes(target)} onChange={() => toggleTarget(target)} /><span><strong>{targetLabels[target]}</strong><small>Target-specific evidence only</small></span></label>)}
          </div>
            <div className={styles.methodChoice} aria-labelledby="research-method-heading">
              <div><p className={styles.eyebrow}>RESEARCH METHOD</p><h3 id="research-method-heading">Improve {targets.length === 1 ? targetLabels[targets[0]] : "selected areas"}</h3><p className={styles.contextNote}>RAVEN Research is the fast integrated evidence workflow. Deep Research runs asynchronously and becomes reviewable for profile improvement after an accepted Profile v1 exists. External AI Assist brings in outside material for review.</p></div>
              <article className={styles.methodChoiceRecommended}><div><strong>Research with RAVEN</strong><span>Search and verify sources using RAVEN's normal evidence workflow.</span></div><button className="button" type="button" onClick={() => void start()} disabled={loading || targets.length === 0 || !hasUsableAcceptedProfile(profile)}>Find selected information</button></article>
              {!hasUsableAcceptedProfile(profile) ? <p className={styles.contextNote} role="status">Create or repair the initial profile before applying targeted evidence. Deep Research may still run in the background, but its completed result stays locked until a supported profile exists.</p> : null}
              <div className={styles.methodChoiceAlternatives}><button className="button button--secondary" type="button" onClick={() => void launchDeepResearch()} disabled={loading || targets.length === 0}><Sparkle size={16} weight="fill" /> Deep Research <small>Broader background investigation</small></button><button className="button button--secondary" type="button" onClick={() => { if (targets.length > 0) { onClose(); onOpenExternalResearch?.(targets); } }} disabled={loading || targets.length === 0}><ArrowSquareOut size={16} weight="bold" /> External AI Assist <small>Generate a brief for all selected areas</small></button></div>
            </div>
          <div className="form-actions"><button className="button button--secondary" type="button" onClick={close}>Cancel</button></div>
        </section>}

        {(phase === "discovering" || phase === "acquiring" || phase === "generatingPatch") && <section className={styles.enrichmentSection} aria-live="polite"><h3>{phase === "discovering" ? materialArtifactId || managedInvestigationId ? "Reviewing investigation findings" : "Finding targeted evidence" : phase === "acquiring" ? "Acquiring selected evidence" : "Preparing profile update"}</h3><p className={styles.contextNote}>{materialArtifactId || managedInvestigationId ? "RAVEN is comparing the supplied findings with the accepted profile. No new search or crawl is running." : `RAVEN is working on ${targets.map((target) => targetLabels[target]).join(", ")}.`}</p><div className={styles.enrichmentProgress} role="status">Working…</div></section>}

        {phase === "reviewingSources" && <section className={styles.enrichmentSection} aria-labelledby="targeted-sources-heading"><div className={styles.sectionHeader}><h3 id="targeted-sources-heading">Review targeted candidates</h3><span>{selectedCandidates} selected · {candidates.length} found</span></div><p className={styles.contextNote}>These roots were selected for the approved gaps. Review them before they become evidence.</p><div className={styles.enrichmentCandidateGrid}>{candidates.map((candidate) => <CandidateSourceCard key={candidate.id} candidate={toCandidateSource(candidate)} disabled={loading} onSelectionChange={(selected) => setCandidates((current) => current.map((item) => item.id === candidate.id ? { ...item, selected } : item))} />)}</div><div className="form-actions"><button className="button" type="button" onClick={() => void acquire()} disabled={loading || selectedCandidates === 0}>Acquire {selectedCandidates} evidence root{selectedCandidates === 1 ? "" : "s"}</button><button className="button button--secondary" type="button" onClick={() => setPhase("choose")}>Back to targets</button></div></section>}

        {phase === "reviewingEvidence" && <section className={styles.enrichmentSection} aria-labelledby="targeted-evidence-heading"><div className={styles.sectionHeader}><h3 id="targeted-evidence-heading">Review acquired evidence</h3><span>{sources.length} document{sources.length === 1 ? "" : "s"} acquired</span></div><div className={styles.enrichmentEvidenceGrid}>{sources.map((source) => <EvidenceCard key={source.id} evidence={toEvidenceRecord(source)} />)}</div>{sources.length === 0 && <p className={styles.contextNote}>No documents were acquired. RAVEN will not invent a patch.</p>}<div className="form-actions"><button className="button" type="button" onClick={() => void generatePatch()} disabled={loading || sources.length === 0}>Generate profile update</button><button className="button button--secondary" type="button" onClick={() => setPhase("reviewingSources")}>Back to candidates</button></div></section>}

        {phase === "reviewingPatch" && patch && <section className={styles.enrichmentSection} aria-labelledby="profile-update-heading"><div className={styles.sectionHeader}><div><p className={styles.eyebrow}>{materialArtifactId || managedInvestigationId ? "PROFILE IMPROVEMENT REVIEW" : "PROFILE UPDATE"}</p><h3 id="profile-update-heading">{materialArtifactId || managedInvestigationId ? "What RAVEN would improve" : "Profile update"}</h3></div><span>{materialArtifactId || managedInvestigationId ? "Review before applying" : "Server-generated review"}</span></div><p className={styles.contextNote}>{materialArtifactId || managedInvestigationId ? "This proposal is based on the investigation material and RAVEN’s comparison with the accepted profile. Confirming creates a new immutable profile version." : "Only the returned changes can be confirmed. The client cannot edit field paths or proposed values."}</p>{patch.changes.length ? <div className={styles.patchList}>{patch.changes.map((change) => <article className={styles.patchChange} key={change.fieldPath}><h4>{fieldLabel(change.fieldPath)}</h4><dl><div><dt>Current</dt><dd>{prettyValue(change.oldValue)}</dd></div><div><dt>Proposed</dt><dd>{prettyValue(change.proposedValue)}</dd></div></dl>{change.evidenceSourceDocumentIds.length ? <p className={styles.patchEvidence}>Based on {change.evidenceSourceDocumentIds.length} supplied evidence reference{change.evidenceSourceDocumentIds.length === 1 ? "" : "s"}.</p> : <p className={styles.patchEvidence}>No attached source reference — review this change carefully.</p>}</article>)}</div> : <div className={styles.emptyState}><h4>No supported changes</h4><p>RAVEN found no evidence strong enough to propose an update. Existing profile values remain unchanged.</p></div>}<section className={styles.unchangedPanel} aria-labelledby="unchanged-fields-heading"><h4 id="unchanged-fields-heading">Unchanged fields</h4><p>{unchangedFields.map((target) => targetLabels[target]).join(" · ") || "All other accepted fields"}</p></section>{patch.warnings.length ? <div className={styles.enrichmentWarnings} role="status"><h4>Review notes</h4><ul>{patch.warnings.map((warning) => <li key={warning}>{warning}</li>)}</ul></div> : null}<div className="form-actions"><button className="button" type="button" onClick={() => void requestConfirmPatch()} disabled={loading || !patch.candidateId || patch.changes.length === 0}>{materialArtifactId || managedInvestigationId ? "Improve profile" : "Confirm profile update"}</button>{!materialArtifactId && !managedInvestigationId && <button className="button button--secondary" type="button" onClick={() => setPhase("reviewingEvidence")}>Back to evidence</button>}</div></section>}

        {phase === "reviewingPatch" && showOverlapWarning ? <aside className={styles.enrichmentWarnings} role="alert"><h4>Research for this profile area is still running.</h4><p>This update changes {targets.map((target) => targetLabels[target]).join(" and ")}, and an active investigation is researching the same area.</p><div className="form-actions"><button className="button button--secondary" type="button" onClick={() => setShowOverlapWarning(false)}>Wait for research</button><button className="button" type="button" onClick={() => void confirmPatch()}>Finalize anyway</button></div></aside> : null}
        {phase === "confirmed" && <section className={styles.enrichmentSection} aria-live="polite"><h3>Profile updated</h3><p className={styles.contextNote}>RAVEN created a new immutable Company Profile version. Unrelated accepted fields were preserved.</p><div className="form-actions"><button className="button" type="button" onClick={close}>Return to profile</button></div></section>}
        {phase === "failed" && <section className={styles.enrichmentSection}><h3>Targeted research needs attention</h3><p className={styles.errorMessage} role="alert">{error || "RAVEN could not complete this targeted run."}</p><div className="form-actions"><button className="button button--secondary" type="button" onClick={() => { setError(null); setPhase("choose"); }}>Back to targets</button><button className="button button--quiet" type="button" onClick={close}>Close</button></div></section>}
        {error && phase !== "failed" ? <p className={styles.errorMessage} role="alert">{error}</p> : null}
      </div>
    </dialog>
  );
}
