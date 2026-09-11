import { useEffect, useMemo, useRef, useState } from "react";
import { getApiErrorMessage } from "../../api/client";
import { getResearchCandidates, getResearchSources, acquireResearchCandidates } from "../../api/research";
import {
  confirmProfilePatch,
  generateProfilePatch,
  startTargetedResearch,
  type ProfilePatchCandidate,
} from "../../api/profiles";
import type { ResearchTarget } from "../../api/coverage";
import type { ResearchCandidate, ResearchRun, SourceDocument } from "../../types/research";
import type { CompanyProfileVersion } from "../../types/profile";
import { CandidateSourceCard, EvidenceCard, type CandidateSource, type EvidenceRecord } from "../sources";
import styles from "./dossier.module.css";
import type { DossierCompany, DossierProfile } from "./dossierTypes";

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
  open: boolean;
  onClose: () => void;
  onConfirmed?: (profile: CompanyProfileVersion) => void;
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

function prettyValue(value?: string | null) {
  if (!value) return "Not verified";
  try {
    const parsed = JSON.parse(value) as unknown;
    if (Array.isArray(parsed)) return parsed.length ? parsed.map((item) => typeof item === "object" && item !== null && "name" in item ? String((item as { name?: unknown }).name || "") : String(item)).filter(Boolean).join(", ") : "Not verified";
    return typeof parsed === "string" ? parsed : JSON.stringify(parsed);
  } catch {
    return value;
  }
}

export function TargetedEnrichmentPanel({ company, profile, initialTargets, open, onClose, onConfirmed }: TargetedEnrichmentPanelProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [targets, setTargets] = useState<ResearchTarget[]>(initialTargets);
  const [phase, setPhase] = useState<EnrichmentPhase>("choose");
  const [run, setRun] = useState<ResearchRun | null>(null);
  const [candidates, setCandidates] = useState<ResearchCandidate[]>([]);
  const [sources, setSources] = useState<SourceDocument[]>([]);
  const [patch, setPatch] = useState<ProfilePatchCandidate | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setTargets(initialTargets);
  }, [initialTargets]);

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
    setTargets(initialTargets);
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
      });
      setRun(nextRun);
      await loadCandidates(nextRun);
    } catch (reason) {
      setError(getApiErrorMessage(reason, "RAVEN could not start targeted research."));
      setPhase("failed");
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

  async function generatePatch() {
    if (!run) return;
    setLoading(true);
    setError(null);
    setPhase("generatingPatch");
    try {
      setPatch(await generateProfilePatch(run.id));
      setPhase("reviewingPatch");
    } catch (reason) {
      setError(getApiErrorMessage(reason, "RAVEN could not generate a reviewable profile patch."));
      setPhase("failed");
    } finally {
      setLoading(false);
    }
  }

  async function confirmPatch() {
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

  const selectedCandidates = useMemo(() => candidates.filter((candidate) => candidate.selected).length, [candidates]);
  const changedFields = useMemo(() => new Set((patch?.changes ?? []).map((change) => change.fieldPath)), [patch]);
  const unchangedFields = targetOrder.filter((target) => !patch?.allowedTargets.includes(target) && !["LegalIdentity", "TaxRegistration", "FoundedHistory", "Industry", "EmployeeScale", "ProductsServices", "Markets", "Leadership", "Locations"].some((item) => item === target && [...changedFields].some((field) => field.toLowerCase().includes(target.toLowerCase().replace("services", "")))));

  return (
    <dialog ref={dialogRef} className={styles.enrichmentDialog} aria-labelledby="targeted-enrichment-heading" onCancel={close}>
      <div className={styles.enrichmentDialogBody}>
        <header className={styles.enrichmentHeader}>
          <div><p className={styles.eyebrow}>TARGETED RESEARCH</p><h2 id="targeted-enrichment-heading">Strengthen {company.displayName}</h2><p className={styles.contextNote}>RAVEN will research only the areas you approve, then present a server-generated patch for review.</p></div>
          <button className="button button--quiet" type="button" onClick={close} aria-label="Close targeted research">Close</button>
        </header>

        {phase === "choose" && <section className={styles.enrichmentSection} aria-labelledby="targeted-areas-heading">
          <h3 id="targeted-areas-heading">What should RAVEN strengthen?</h3>
          <p className={styles.contextNote}>Choose one or more missing or weak areas. Unselected accepted fields cannot be changed by this run.</p>
          <div className={styles.targetGrid}>
            {targetOrder.map((target) => <label className={styles.targetOption} key={target}><input type="checkbox" checked={targets.includes(target)} onChange={() => toggleTarget(target)} /><span><strong>{targetLabels[target]}</strong><small>Target-specific evidence only</small></span></label>)}
          </div>
          <div className="form-actions"><button className="button" type="button" onClick={() => void start()} disabled={loading || targets.length === 0}>Find selected information</button><button className="button button--secondary" type="button" onClick={close}>Cancel</button></div>
        </section>}

        {(phase === "discovering" || phase === "acquiring" || phase === "generatingPatch") && <section className={styles.enrichmentSection} aria-live="polite"><h3>{phase === "discovering" ? "Finding targeted evidence" : phase === "acquiring" ? "Acquiring selected evidence" : "Preparing profile update"}</h3><p className={styles.contextNote}>RAVEN is working on {targets.map((target) => targetLabels[target]).join(", ")}.</p><div className={styles.enrichmentProgress} role="status">Working…</div></section>}

        {phase === "reviewingSources" && <section className={styles.enrichmentSection} aria-labelledby="targeted-sources-heading"><div className={styles.sectionHeader}><h3 id="targeted-sources-heading">Review targeted candidates</h3><span>{selectedCandidates} selected · {candidates.length} found</span></div><p className={styles.contextNote}>These roots were selected for the approved gaps. Review them before they become evidence.</p><div className={styles.enrichmentCandidateGrid}>{candidates.map((candidate) => <CandidateSourceCard key={candidate.id} candidate={toCandidateSource(candidate)} disabled={loading} onSelectionChange={(selected) => setCandidates((current) => current.map((item) => item.id === candidate.id ? { ...item, selected } : item))} />)}</div><div className="form-actions"><button className="button" type="button" onClick={() => void acquire()} disabled={loading || selectedCandidates === 0}>Acquire {selectedCandidates} evidence root{selectedCandidates === 1 ? "" : "s"}</button><button className="button button--secondary" type="button" onClick={() => setPhase("choose")}>Back to targets</button></div></section>}

        {phase === "reviewingEvidence" && <section className={styles.enrichmentSection} aria-labelledby="targeted-evidence-heading"><div className={styles.sectionHeader}><h3 id="targeted-evidence-heading">Review acquired evidence</h3><span>{sources.length} document{sources.length === 1 ? "" : "s"} acquired</span></div><div className={styles.enrichmentEvidenceGrid}>{sources.map((source) => <EvidenceCard key={source.id} evidence={toEvidenceRecord(source)} />)}</div>{sources.length === 0 && <p className={styles.contextNote}>No documents were acquired. RAVEN will not invent a patch.</p>}<div className="form-actions"><button className="button" type="button" onClick={() => void generatePatch()} disabled={loading || sources.length === 0}>Generate profile update</button><button className="button button--secondary" type="button" onClick={() => setPhase("reviewingSources")}>Back to candidates</button></div></section>}

        {phase === "reviewingPatch" && patch && <section className={styles.enrichmentSection} aria-labelledby="profile-update-heading"><div className={styles.sectionHeader}><h3 id="profile-update-heading">Profile update</h3><span>Server-generated review</span></div><p className={styles.contextNote}>Only the returned changes can be confirmed. The client cannot edit field paths or proposed values.</p>{patch.changes.length ? <div className={styles.patchList}>{patch.changes.map((change) => <article className={styles.patchChange} key={change.fieldPath}><h4>{fieldLabel(change.fieldPath)}</h4><dl><div><dt>Current</dt><dd>{prettyValue(change.oldValue)}</dd></div><div><dt>Proposed</dt><dd>{prettyValue(change.proposedValue)}</dd></div></dl>{change.evidenceSourceDocumentIds.length ? <p className={styles.patchEvidence}>Evidence references: {change.evidenceSourceDocumentIds.length}</p> : <p className={styles.patchEvidence}>The server returned no source reference for this change.</p>}</article>)}</div> : <div className={styles.emptyState}><h4>No supported changes</h4><p>RAVEN found no evidence strong enough to propose an update. Existing profile values remain unchanged.</p></div>}<section className={styles.unchangedPanel} aria-labelledby="unchanged-fields-heading"><h4 id="unchanged-fields-heading">Unchanged fields</h4><p>{unchangedFields.map((target) => targetLabels[target]).join(" · ") || "All other accepted fields"}</p></section>{patch.warnings.length ? <div className={styles.enrichmentWarnings} role="status"><h4>Review notes</h4><ul>{patch.warnings.map((warning) => <li key={warning}>{warning}</li>)}</ul></div> : null}<div className="form-actions"><button className="button" type="button" onClick={() => void confirmPatch()} disabled={loading || !patch.candidateId || patch.changes.length === 0}>Confirm profile update</button><button className="button button--secondary" type="button" onClick={() => setPhase("reviewingEvidence")}>Back to evidence</button></div></section>}

        {phase === "confirmed" && <section className={styles.enrichmentSection} aria-live="polite"><h3>Profile updated</h3><p className={styles.contextNote}>RAVEN created a new immutable Company Profile version. Unrelated accepted fields were preserved.</p><div className="form-actions"><button className="button" type="button" onClick={close}>Return to profile</button></div></section>}
        {phase === "failed" && <section className={styles.enrichmentSection}><h3>Targeted research needs attention</h3><p className={styles.errorMessage} role="alert">{error || "RAVEN could not complete this targeted run."}</p><div className="form-actions"><button className="button button--secondary" type="button" onClick={() => { setError(null); setPhase("choose"); }}>Back to targets</button><button className="button button--quiet" type="button" onClick={close}>Close</button></div></section>}
        {error && phase !== "failed" ? <p className={styles.errorMessage} role="alert">{error}</p> : null}
      </div>
    </dialog>
  );
}
