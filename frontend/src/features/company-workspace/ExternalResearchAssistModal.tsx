import { useEffect, useMemo, useRef, useState } from "react";
import { ArrowLeft, ArrowRight, ClipboardText, FileArrowUp, Minus, Sparkle, X } from "@phosphor-icons/react";
import { getApiErrorMessage } from "../../api/client";
import {
  generateExternalResearchBrief,
  getExternalResearchAnalysis,
  importExternalResearch,
  startExternalResearchAnalysis,
  type ExternalResearchBrief,
  type ExternalResearchImportPreview,
} from "../../api/externalResearch";
import type { ResearchTarget } from "../../api/coverage";
import { dismissResearchActivity, upsertResearchActivity, updateResearchActivity } from "../../utils/researchActivity";
import styles from "./dossier.module.css";
import type { DossierCompany, DossierProfile } from "./dossierTypes";

type AssistStage = "brief" | "paste" | "review";

export interface ExternalResearchAssistModalProps {
  company: DossierCompany;
  profile?: DossierProfile | null;
  targets?: ResearchTarget[] | null;
  objective?: string;
  open: boolean;
  onClose: () => void;
  onSaved?: () => void | Promise<void>;
}

const targetLabels: Record<ResearchTarget, string> = {
  LegalIdentity: "Legal identity",
  TaxRegistration: "Tax registration",
  FoundedHistory: "Founded / history",
  Industry: "Industry",
  EmployeeScale: "Employee scale",
  ProductsServices: "Products & services",
  Markets: "Markets",
  Leadership: "Leadership",
  Locations: "Locations",
};

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

interface StoredDraft {
  stage: AssistStage;
  brief: ExternalResearchBrief | null;
  prompt: string;
  question: string;
  response: string;
  preview: ExternalResearchImportPreview | null;
  analysisJobId?: string | null;
}

function targetSignature(targets: ResearchTarget[]) {
  return targets.length > 0 ? targets.join("+") : "general";
}

function activityId(companyId: string, targets: ResearchTarget[]) {
  return `external-research-${companyId}-${targetSignature(targets)}`;
}

function draftKey(companyId: string, targets: ResearchTarget[], profileKey: string) {
  return `raven-external-research-v2-${companyId}-${targetSignature(targets)}-${profileKey}`;
}

function readDraft(key: string): StoredDraft | null {
  try {
    const raw = window.sessionStorage.getItem(key);
    return raw ? JSON.parse(raw) as StoredDraft : null;
  } catch {
    return null;
  }
}

function writeDraft(key: string, draft: StoredDraft) {
  try {
    window.sessionStorage.setItem(key, JSON.stringify(draft));
  } catch {
    // Draft persistence is best effort; the in-memory state remains usable.
  }
}

function formatCount(count: number, singular: string) {
  return `${count} ${singular}${count === 1 ? "" : "s"}`;
}

function closeDialog(dialog: HTMLDialogElement | null) {
  if (!dialog) return;
  if (typeof dialog.close === "function") dialog.close();
  else dialog.removeAttribute("open");
}

export function ExternalResearchAssistModal({ company, profile, targets: requestedTargets, objective: requestedObjective, open, onClose, onSaved }: ExternalResearchAssistModalProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const initializedKeyRef = useRef<string | null>(null);
  const dismissedActivityRef = useRef(false);
  const [stage, setStage] = useState<AssistStage>("brief");
  const [brief, setBrief] = useState<ExternalResearchBrief | null>(null);
  const [prompt, setPrompt] = useState("");
  const [question, setQuestion] = useState("");
  const [response, setResponse] = useState("");
  const [preview, setPreview] = useState<ExternalResearchImportPreview | null>(null);
  const [analysisJobId, setAnalysisJobId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [copied, setCopied] = useState(false);
  const [minimized, setMinimized] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const targets = useMemo(() => [...new Set(requestedTargets ?? [])].sort((left, right) => targetOrder.indexOf(left) - targetOrder.indexOf(right)), [requestedTargets]);
  const targetKey = targetSignature(targets);
  const profileKey = profile?.version ? `v${profile.version}` : profile?.id ? `id-${profile.id}` : "none";
  const objective = (requestedObjective?.trim() || (targets.length > 0 ? `Current ${targets.map((target) => targetLabels[target]).join(", ")}` : "A focused company research question"));
  const key = draftKey(company.id, targets, profileKey);
  const id = activityId(company.id, targets);
  const displayTarget = targets.length > 0 ? targets.map((target) => targetLabels[target]).join(", ") : null;

  const persist = () => writeDraft(key, { stage, brief, prompt, question, response, preview, analysisJobId });

  useEffect(() => {
    if (!open) initializedKeyRef.current = null;
  }, [open]);

  useEffect(() => {
    if (!open || initializedKeyRef.current === key) return;
    initializedKeyRef.current = key;
    dismissedActivityRef.current = false;
    const draft = readDraft(key);
    if (draft) {
      setStage(draft.stage);
      setBrief(draft.brief);
      setPrompt(draft.prompt);
      setQuestion(draft.question || objective);
      setResponse(draft.response);
      setPreview(draft.preview);
      setAnalysisJobId(draft.analysisJobId || null);
      setBusy(Boolean(draft.analysisJobId && !draft.preview));
      if (draft.preview) {
        upsertResearchActivity({ id, jobId: draft.analysisJobId || undefined, origin: "External", companyId: company.id, companyName: company.displayName, objective, detail: "Ready for review", status: "ready", href: `/companies/${encodeURIComponent(company.id)}?tab=investigations`, onOpen: () => setMinimized(false), updatedAt: new Date().toISOString() });
      } else if (draft.analysisJobId) {
        upsertResearchActivity({ id, jobId: draft.analysisJobId || undefined, origin: "External", companyId: company.id, companyName: company.displayName, objective, detail: "Reading imported response", status: "running", href: `/companies/${encodeURIComponent(company.id)}?tab=investigations`, onOpen: () => setMinimized(false), updatedAt: new Date().toISOString() });
      }
      return;
    }

    setStage("brief");
    setBrief(null);
    setPrompt("");
    setQuestion(objective);
    setResponse("");
    setPreview(null);
    setAnalysisJobId(null);
    setError(null);
    setMessage(null);
    setBusy(true);
    upsertResearchActivity({ id, origin: "External", companyId: company.id, companyName: company.displayName, objective, detail: "Preparing a focused research brief", status: "running", href: `/companies/${encodeURIComponent(company.id)}?tab=investigations`, onOpen: () => setMinimized(false), updatedAt: new Date().toISOString() });
    void generateExternalResearchBrief(company.id, { researchObjective: objective, requestedTargets: targets.length > 0 ? targets : undefined })
      .then((result) => {
        setBrief(result);
        setPrompt(result.markdown);
        setQuestion(result.objective || objective);
        if (!dismissedActivityRef.current) updateResearchActivity(id, { detail: "Brief ready to copy", status: "ready", onOpen: () => setMinimized(false) });
      })
      .catch((reason: unknown) => {
        setError(getApiErrorMessage(reason, "RAVEN could not prepare this research brief."));
        if (!dismissedActivityRef.current) updateResearchActivity(id, { detail: "Brief could not be prepared", status: "failed" });
      })
      .finally(() => setBusy(false));
  }, [company.displayName, company.id, key, objective, open, targetKey, targets]);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !minimized) {
      if (!dialog.open && typeof dialog.showModal === "function") dialog.showModal();
      else if (!dialog.open) dialog.setAttribute("open", "");
    } else if (dialog.open) {
      closeDialog(dialog);
    }
  }, [minimized, open]);

  useEffect(() => {
    if (!analysisJobId) return;
    let active = true;
    const poll = async () => {
      try {
        const job = await getExternalResearchAnalysis(company.id, analysisJobId);
        if (!active) return;
        if (job.status === "Completed" && job.result) {
          setPreview(job.result);
          setStage("review");
          setAnalysisJobId(null);
          setBusy(false);
          if (!dismissedActivityRef.current) updateResearchActivity(id, { detail: "Ready for review", status: "ready", onOpen: () => setMinimized(false) });
          return;
        }
        if (job.status === "Failed") {
          setBusy(false);
          setAnalysisJobId(null);
          setError(job.error || "RAVEN couldn't fully analyze this response. Your pasted research has been preserved.");
          if (!dismissedActivityRef.current) updateResearchActivity(id, { detail: "Analysis failed; pasted material is preserved", status: "failed" });
        }
      } catch (reason: unknown) {
        if (active) setError(getApiErrorMessage(reason, "RAVEN could not check the analysis status."));
      }
    };
    void poll();
    const intervalId = window.setInterval(() => { void poll(); }, 1_000);
    return () => { active = false; window.clearInterval(intervalId); };
  }, [analysisJobId, company.id, id]);

  useEffect(() => {
    if (!open) return;
    writeDraft(key, { stage, brief, prompt, question, response, preview, analysisJobId });
  }, [analysisJobId, brief, key, open, preview, prompt, question, response, stage]);

  const close = () => {
    persist();
    setMinimized(false);
    closeDialog(dialogRef.current);
    onClose();
  };

  const minimize = () => {
    persist();
    setMinimized(true);
    closeDialog(dialogRef.current);
    upsertResearchActivity({ id, jobId: analysisJobId || undefined, origin: "External", companyId: company.id, companyName: company.displayName, objective, detail: preview ? "Ready for review" : "Analysis continues in the background", status: preview ? "ready" : "running", href: `/companies/${encodeURIComponent(company.id)}?tab=investigations`, onOpen: () => setMinimized(false), updatedAt: new Date().toISOString() });
  };

  const copyPrompt = async () => {
    try {
      await navigator.clipboard.writeText(prompt);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 1800);
    } catch {
      setMessage("Brief ready. Select and copy it from the text area.");
    }
  };

  const analyze = async () => {
    if (!response.trim()) return;
    setBusy(true);
    setError(null);
    setMessage(null);
    updateResearchActivity(id, { detail: "Reading imported response", status: "running", onOpen: () => setMinimized(false) });
    try {
      const job = await startExternalResearchAnalysis(company.id, { question: question.trim() || objective, markdown: response });
      setAnalysisJobId(job.id);
      updateResearchActivity(id, { jobId: job.id });
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "RAVEN couldn't fully analyze this response. Your pasted research has been preserved."));
      if (!dismissedActivityRef.current) updateResearchActivity(id, { detail: "Analysis failed; pasted material is preserved", status: "failed" });
    } finally {
      setBusy(false);
    }
  };

  const save = async () => {
    if (!preview) return;
    setBusy(true);
    setError(null);
    try {
      await importExternalResearch(company.id, { question: question.trim() || objective, markdown: response });
      setMessage("Saved to Investigation as unverified research material.");
      dismissResearchActivity(id);
      await onSaved?.();
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "RAVEN could not save this research material."));
    } finally {
      setBusy(false);
    }
  };

  const summaryCounts = useMemo(() => ({ claims: preview?.claims.length ?? 0, sources: preview?.sourceLeads.length ?? 0, uncertainties: preview?.uncertainties.length ?? 0 }), [preview]);
  const reviewCounts = useMemo(() => {
    const claims = preview?.claims ?? [];
    const acceptedFields = new Set([
      profile?.legalName ? "legal" : "",
      profile?.registrationNumberOrTaxId ? "tax" : "",
      profile?.foundedYear ? "founded" : "",
      profile?.primaryIndustry ? "industry" : "",
      profile?.companySize || profile?.employeeCount || profile?.employeeCountRange ? "employee" : "",
      profile?.productsServices?.length ? "product" : "",
      profile?.markets?.length ? "market" : "",
      profile?.leadership?.length ? "leadership" : "",
      profile?.locations?.length || profile?.headquarters ? "location" : "",
    ]);
    const overlaps = claims.filter((claim) => [...acceptedFields].some((field) => field && claim.field.toLowerCase().includes(field))).length;
    return { overlaps, needsHumanReview: claims.filter((claim) => !claim.supportingSourceLeadIds?.length).length };
  }, [preview, profile]);

  return (
    <dialog ref={dialogRef} className={styles.externalAssistDialog} aria-labelledby="external-assist-heading" onCancel={(event) => { event.preventDefault(); close(); }}>
      <div className={styles.externalAssistBody}>
        <header className={styles.externalAssistHeader}>
          <div><p className={styles.eyebrow}>EXTERNAL AI ASSIST</p><h2 id="external-assist-heading">External AI Assist{displayTarget ? ` — ${displayTarget}` : ""}</h2><p className={styles.contextNote}>For {company.displayName}. The assistant's research is preserved for AI-assisted and human review; RAVEN will not re-search these links.</p></div>
          <div className={styles.externalAssistHeaderActions}>
            <button className={styles.externalAssistHeaderIconButton} type="button" onClick={minimize} aria-label="Minimize External AI Assist" title="Minimize"><Minus size={18} weight="bold" /></button>
            <button className={styles.externalAssistHeaderIconButton} type="button" onClick={close} aria-label="Close External AI Assist" title="Close"><X size={18} weight="bold" /></button>
          </div>
        </header>

        {stage === "brief" && <section className={styles.externalAssistSection} aria-labelledby="external-brief-heading">
          <h3 id="external-brief-heading">Research brief</h3>
          <p>Use your preferred web-enabled AI assistant to investigate this focused objective. RAVEN has included the company identity and current evidence gap so strong fields are not re-researched unnecessarily.</p>
          <label className={styles.externalResearchField}><span>Copyable prompt</span><textarea value={prompt} onChange={(event) => setPrompt(event.target.value)} rows={12} disabled={busy && !prompt} /></label>
          <div className={styles.externalAssistActions}><button className="button button--secondary" type="button" onClick={() => void copyPrompt()} disabled={!prompt || busy}><ClipboardText size={17} weight="bold" /> {copied ? "Copied" : "Copy prompt"}</button><button className="button" type="button" onClick={() => { persist(); setStage("paste"); }} disabled={!prompt}><ArrowRight size={17} weight="bold" /> I've got a response</button></div>
          {busy && !prompt ? <p className={styles.contextNote} role="status">Preparing your focused brief…</p> : null}
        </section>}

        {stage === "paste" && <section className={styles.externalAssistSection} aria-labelledby="external-paste-heading">
          <h3 id="external-paste-heading">Paste the assistant's response</h3>
          <p>Preserve source URLs where possible. RAVEN analyzes the supplied material before anything is saved.</p>
          <label className={styles.externalResearchField}><span>Research question</span><input value={question} onChange={(event) => setQuestion(event.target.value)} /></label>
          <label className={styles.externalResearchField}><span>Assistant response</span><textarea value={response} onChange={(event) => { setResponse(event.target.value); setPreview(null); setAnalysisJobId(null); }} rows={16} placeholder="Paste response here…" autoFocus /></label>
          <div className={styles.externalAssistActions}><button className="button button--secondary" type="button" onClick={() => setStage("brief")}><ArrowLeft size={17} weight="bold" /> Back</button><button className="button" type="button" onClick={() => void analyze()} disabled={busy || !response.trim()}>{busy ? "Analyzing…" : "Analyze"}</button></div>
          {busy ? <p className={styles.contextNote} role="status">Analyzing imported research… You can minimize this window while it continues.</p> : null}
        </section>}

        {stage === "review" && preview && <section className={styles.externalAssistSection} aria-labelledby="external-review-heading">
          <div className={styles.externalAssistReviewHeading}><div><h3 id="external-review-heading">Review imported research</h3><p className={styles.contextNote}>{formatCount(summaryCounts.claims, "claim")} · {formatCount(summaryCounts.sources, "source lead")} · {formatCount(summaryCounts.uncertainties, "uncertainty")}</p></div><span className={styles.unverifiedBadge}><FileArrowUp size={15} weight="bold" /> Unverified research material</span></div>
          {preview.summary ? <article className={styles.externalAssistSummary}><h4>Research summary</h4><p>{preview.summary}</p></article> : null}
          <article className={styles.externalAssistReviewSummary}><div><h4>RAVEN review</h4><p>The supplied AI result is compared with the accepted profile before any change is proposed. No second source crawl is started.</p></div><div className={styles.externalAssistReviewStats}><span><strong>{reviewCounts.overlaps}</strong> overlap accepted fields</span><span><strong>{reviewCounts.needsHumanReview}</strong> need human scrutiny</span></div></article>
          <div className={styles.externalAssistReviewGrid}>
            <div><h4>Claims</h4>{preview.claims.length ? <ul className={styles.plainList}>{preview.claims.map((claim, index) => <li key={`${claim.field}-${index}`}><strong>{claim.field}</strong><span>{claim.statement}</span>{claim.notes ? <small>{claim.notes}</small> : null}<em>{claim.supportingSourceLeadIds?.length ? `${claim.supportingSourceLeadIds.length} source lead${claim.supportingSourceLeadIds.length === 1 ? "" : "s"}` : "No supporting URL"}</em></li>)}</ul> : <p className={styles.contextNote}>No structured claims were detected.</p>}</div>
            <div><h4>Uncertainties</h4>{preview.uncertainties.length ? <ul className={styles.plainList}>{preview.uncertainties.map((item) => <li key={item}>{item}</li>)}</ul> : <p className={styles.contextNote}>No uncertainties were provided.</p>}</div>
          </div>
          <div><h4>Source leads</h4>{preview.sourceLeads.length ? <ul className={styles.externalSourceLeadList}>{preview.sourceLeads.map((lead) => <li key={lead.id}><span><a href={lead.url} target="_blank" rel="noreferrer">{lead.title || lead.url}</a><small>{lead.publisher || lead.url}</small></span></li>)}</ul> : <p className={styles.contextNote}>No source leads were detected. Save this material as research notes and use the review findings to decide whether it is strong enough for a profile proposal.</p>}</div>
          <div className={styles.externalAssistActions}><button className="button button--secondary" type="button" onClick={() => setStage("paste")}><ArrowLeft size={17} weight="bold" /> Back to response</button><button className="button" type="button" onClick={() => void save()} disabled={busy}>{busy ? "Saving…" : "Save to Investigation"}</button></div>
        </section>}

        {message ? <p className={styles.successMessage} role="status">{message}</p> : null}
        {error ? <p className={styles.errorMessage} role="alert">{error}</p> : null}
        <footer className={styles.externalAssistFooter}><Sparkle size={15} weight="fill" aria-hidden="true" /> External AI output is research material. Human confirmation is required before a profile change.</footer>
      </div>
    </dialog>
  );
}
