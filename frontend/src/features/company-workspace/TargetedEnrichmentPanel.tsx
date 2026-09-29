import { useEffect, useMemo, useRef, useState } from "react";
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
import { CandidateSourceCard, EvidenceCard } from "../../components/sources";
import styles from "./company-workspace.module.css";
import type { DossierCompany, DossierProfile } from "./dossierTypes";
import { upsertResearchActivity } from "../../utils/researchActivity";
import { hasUsableAcceptedProfile } from "../../utils/profileReadiness";
import { fieldLabel, prettyValue, targetLabels, targetOrder, toCandidateSource, toEvidenceRecord } from "./enrichment/enrichmentMapping";
import { EnrichmentTargetSelector } from "./enrichment/EnrichmentTargetSelector";
import { EnrichmentCandidateReview } from "./enrichment/EnrichmentCandidateReview";


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

{phase === "choose" && !materialArtifactId && !managedInvestigationId && <EnrichmentTargetSelector targets={targets} toggleTarget={toggleTarget} profile={profile} loading={loading} start={start} launchDeepResearch={launchDeepResearch} onClose={onClose} onOpenExternalResearch={onOpenExternalResearch} close={close} />}

        {(phase === "discovering" || phase === "acquiring" || phase === "generatingPatch") && <section className={styles.enrichmentSection} aria-live="polite"><h3>{phase === "discovering" ? materialArtifactId || managedInvestigationId ? "Reviewing investigation findings" : "Finding targeted evidence" : phase === "acquiring" ? "Acquiring selected evidence" : "Preparing profile update"}</h3><p className={styles.contextNote}>{materialArtifactId || managedInvestigationId ? "RAVEN is comparing the supplied findings with the accepted profile. No new search or crawl is running." : `RAVEN is working on ${targets.map((target) => targetLabels[target]).join(", ")}.`}</p><div className={styles.enrichmentProgress} role="status">Working…</div></section>}

        {phase === "reviewingSources" && <EnrichmentCandidateReview candidates={candidates} selectedCandidates={selectedCandidates} loading={loading} setCandidates={setCandidates} acquire={acquire} onBack={() => setPhase("choose")} />}

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
