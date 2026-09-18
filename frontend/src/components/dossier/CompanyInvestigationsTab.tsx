import { useEffect, useState } from "react";
import { getApiErrorMessage } from "../../api/client";
import { getSavedInvestigations, type SavedResearchArtifact } from "../../api/investigations";
import { getManagedResearchJobs, type ManagedResearchJob } from "../../api/managedResearch";
import { generateExternalResearchBrief, importExternalResearch, previewExternalResearchImport, type ExternalResearchBrief, type ExternalResearchImportPreview } from "../../api/externalResearch";
import styles from "./dossier.module.css";
import type { DossierInvestigations } from "./dossierTypes";

export interface CompanyInvestigationsTabProps {
  companyId: string;
  investigations?: DossierInvestigations | null;
}

function formatDate(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "Date unavailable";
  return new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short", year: "numeric" }).format(date);
}

function researchTypeLabel(type: SavedResearchArtifact["researchType"]) {
  return type === "Deep" ? "Deep Research" : "Research";
}

function artifactCountLabel(count: number) {
  return `${count} source${count === 1 ? "" : "s"}`;
}

export function CompanyInvestigationsTab({ companyId, investigations }: CompanyInvestigationsTabProps) {
  const [localArtifacts, setLocalArtifacts] = useState<SavedResearchArtifact[] | null>(null);
  const [localLoading, setLocalLoading] = useState(!investigations?.artifacts);
  const [localError, setLocalError] = useState<string | null>(null);
  const [managedJobs, setManagedJobs] = useState<ManagedResearchJob[]>([]);
  const [externalObjective, setExternalObjective] = useState("");
  const [externalBrief, setExternalBrief] = useState<ExternalResearchBrief | null>(null);
  const [externalMarkdown, setExternalMarkdown] = useState("");
  const [externalQuestion, setExternalQuestion] = useState("");
  const [externalPreview, setExternalPreview] = useState<ExternalResearchImportPreview | null>(null);
  const [externalBusy, setExternalBusy] = useState(false);
  const [externalMessage, setExternalMessage] = useState<string | null>(null);
  const [externalError, setExternalError] = useState<string | null>(null);
  const [importedArtifacts, setImportedArtifacts] = useState<SavedResearchArtifact[]>([]);

  useEffect(() => {
    if (investigations?.artifacts) return;
    let active = true;
    setLocalLoading(true);
    setLocalError(null);
    getSavedInvestigations(companyId)
      .then((result) => { if (active) setLocalArtifacts(result); })
      .catch((reason: unknown) => { if (active) setLocalError(getApiErrorMessage(reason, "Could not load saved investigations.")); })
      .finally(() => { if (active) setLocalLoading(false); });
    return () => { active = false; };
  }, [companyId, investigations?.artifacts]);

  useEffect(() => {
    let active = true;
    void getManagedResearchJobs(companyId).then((jobs) => {
      if (active) setManagedJobs(jobs);
    }).catch(() => {
      // Saved investigations remain available if managed research is unavailable.
    });
    return () => { active = false; };
  }, [companyId]);

  const artifacts = investigations?.artifacts ?? localArtifacts ?? [];
  const isLoading = investigations?.isLoading ?? localLoading;
  const error = investigations?.error ?? localError;
  const completedManagedJobs = managedJobs.filter((job) => job.status === "Completed" || job.status === "Researching" || job.status === "Queued");
  const visibleArtifacts = [...importedArtifacts, ...artifacts.filter((artifact) => !importedArtifacts.some((item) => item.id === artifact.id))];

  const prepareExternalBrief = async () => {
    setExternalBusy(true);
    setExternalError(null);
    setExternalMessage(null);
    try {
      const result = await generateExternalResearchBrief(companyId, { researchObjective: externalObjective.trim() || undefined });
      setExternalBrief(result);
      setExternalMarkdown(result.markdown);
      try {
        await navigator.clipboard.writeText(result.markdown);
        setExternalMessage("Brief prepared and copied. Run it in the assistant you prefer, then paste the response below.");
      } catch {
        setExternalMessage("Brief prepared. Copy it from the text area, run it externally, then paste the response below.");
      }
    } catch (reason: unknown) {
      setExternalError(getApiErrorMessage(reason, "Could not prepare an external research brief."));
    } finally {
      setExternalBusy(false);
    }
  };

  const previewExternal = async () => {
    setExternalBusy(true);
    setExternalError(null);
    setExternalMessage(null);
    try {
      const result = await previewExternalResearchImport(companyId, { question: externalQuestion.trim(), markdown: externalMarkdown });
      setExternalPreview(result);
    } catch (reason: unknown) {
      setExternalError(getApiErrorMessage(reason, "Could not parse the pasted research."));
    } finally {
      setExternalBusy(false);
    }
  };

  const saveExternalImport = async () => {
    setExternalBusy(true);
    setExternalError(null);
    setExternalMessage(null);
    try {
      const artifact = await importExternalResearch(companyId, { question: externalQuestion.trim(), markdown: externalMarkdown });
      setImportedArtifacts((current) => [artifact, ...current]);
      setExternalMessage("Saved as reviewable research notes. Nothing was added to the accepted profile.");
      if (investigations?.onRefresh) void investigations.onRefresh();
    } catch (reason: unknown) {
      setExternalError(getApiErrorMessage(reason, "Could not save the external research."));
    } finally {
      setExternalBusy(false);
    }
  };

  const refresh = () => {
    if (investigations?.onRefresh) {
      void investigations.onRefresh();
      return;
    }

    setLocalLoading(true);
    setLocalError(null);
    void getSavedInvestigations(companyId)
      .then(setLocalArtifacts)
      .catch((reason: unknown) => setLocalError(getApiErrorMessage(reason, "Could not load saved investigations.")))
      .finally(() => setLocalLoading(false));
  };

  return (
    <section className={styles.section} data-testid="dossier-investigations" aria-labelledby="dossier-investigations-heading">
      <div className={styles.sectionHeader}>
        <div>
          <h2 id="dossier-investigations-heading">Investigations</h2>
          <p className={styles.tabIntro}>Saved research results remain reference material until a human uses evidence in a profile improvement workflow.</p>
        </div>
        <span>{visibleArtifacts.length + completedManagedJobs.length} available</span>
      </div>

      <div className={styles.externalResearchPanel} data-testid="external-research-import">
        <div>
          <h3>External research</h3>
          <p className={styles.contextNote}>Prepare a focused brief for another assistant, then bring the notes back for review. Imported URLs remain leads until RAVEN acquires and validates them.</p>
        </div>
        <label className={styles.externalResearchField}>
          <span>Optional research objective</span>
          <input value={externalObjective} onChange={(event) => setExternalObjective(event.target.value)} placeholder="e.g. Recent expansion in Japan" />
        </label>
        <button className="button button--secondary" type="button" onClick={() => void prepareExternalBrief()} disabled={externalBusy}>
          {externalBusy && !externalPreview ? "Preparing…" : "Prepare and copy brief"}
        </button>
        {externalBrief && <label className={styles.externalResearchField}>
          <span>Brief (editable before copying)</span>
          <textarea rows={8} value={externalBrief.markdown} onChange={(event) => { setExternalBrief({ ...externalBrief, markdown: event.target.value }); setExternalMarkdown(event.target.value); }} />
        </label>}
        <label className={styles.externalResearchField}>
          <span>Research question</span>
          <input value={externalQuestion} onChange={(event) => setExternalQuestion(event.target.value)} placeholder="What should RAVEN review?" />
        </label>
        <label className={styles.externalResearchField}>
          <span>Paste external response</span>
          <textarea rows={7} value={externalMarkdown} onChange={(event) => { setExternalMarkdown(event.target.value); setExternalPreview(null); }} placeholder="Paste the assistant's Markdown response here…" />
        </label>
        <div className={styles.externalResearchActions}>
          <button className="button button--secondary" type="button" onClick={() => void previewExternal()} disabled={externalBusy || !externalQuestion.trim() || !externalMarkdown.trim()}>Preview notes</button>
          <button className="button" type="button" onClick={() => void saveExternalImport()} disabled={externalBusy || !externalQuestion.trim() || !externalMarkdown.trim() || !externalPreview}>Save to Investigations</button>
        </div>
        {externalMessage && <p className={styles.successMessage} role="status">{externalMessage}</p>}
        {externalError && <p className={styles.errorMessage} role="alert">{externalError}</p>}
        {externalPreview && <div className={styles.externalResearchPreview} data-testid="external-research-preview">
          <strong>Preview — review before saving</strong>
          {externalPreview.summary && <p className={styles.summary}>{externalPreview.summary}</p>}
          {externalPreview.claims.length > 0 && <div><h4>Claims</h4><ul className={styles.plainList}>{externalPreview.claims.map((claim, index) => <li key={`${claim.field}-${index}`}><strong>{claim.field}:</strong> {claim.statement}{claim.notes && <small>{claim.notes}</small>}</li>)}</ul></div>}
          {externalPreview.sourceLeads.length > 0 && <div><h4>Source leads</h4><ul className={styles.plainList}>{externalPreview.sourceLeads.map((source) => <li key={source.id}><a href={source.url} target="_blank" rel="noreferrer">{source.title || source.url}</a>{source.publisher && <small>{source.publisher}</small>}</li>)}</ul></div>}
          {externalPreview.uncertainties.length > 0 && <p className={styles.contextNote}><strong>Uncertainties:</strong> {externalPreview.uncertainties.join(" ")}</p>}
        </div>}
      </div>

      {isLoading && <p className={styles.contextNote} role="status">Loading saved investigations…</p>}
      {error && <p className={styles.errorMessage} role="alert">{error}</p>}
      {!isLoading && !error && !visibleArtifacts.length && !completedManagedJobs.length && (
        <div className={styles.emptyState}>
          <h3>No saved investigations</h3>
          <p>Deep Research results become visible here only after someone explicitly saves them.</p>
        </div>
      )}
      {completedManagedJobs.length > 0 && (
        <div className={styles.investigationList}>
          {completedManagedJobs.map((job) => (
            <article className={styles.investigationCard} key={job.id}>
              <div className={styles.investigationMeta}><span className={styles.investigationType}>Managed AI Research</span><time dateTime={job.createdAt}>{formatDate(job.createdAt)}</time></div>
              <h3>{job.objective}</h3>
              <p className={styles.investigationQuestion}>{job.status === "Completed" ? "Completed research material. Review cited source leads before using it to improve the profile." : "Research is running in the background. You may keep using RAVEN."}</p>
              {job.status === "Completed" && job.result && typeof job.result === "object" && "summary" in job.result ? <details className={styles.investigationDetails}><summary>View research result</summary><div className={styles.investigationResult}>{String((job.result as { summary?: unknown }).summary ?? "No summary returned.")}</div></details> : null}
              <div className={styles.investigationFooter}><span>{job.provider ?? "Managed provider"}</span><span>{job.status}</span></div>
            </article>
          ))}
        </div>
      )}
      {visibleArtifacts.length > 0 && (
        <div className={styles.investigationList}>
          {visibleArtifacts.map((artifact) => (
            <article className={styles.investigationCard} key={artifact.id}>
              <div className={styles.investigationMeta}>
                <span className={styles.investigationType}>{researchTypeLabel(artifact.researchType)}</span>
                <time dateTime={artifact.createdAt}>{formatDate(artifact.createdAt)}</time>
              </div>
              <h3>{artifact.title}</h3>
              <p className={styles.investigationQuestion}><strong>Question:</strong> {artifact.question}</p>
              <details className={styles.investigationDetails}>
                <summary>View saved result</summary>
                <div className={styles.investigationResult}>{artifact.summary || artifact.result || "No saved result text was returned."}</div>
              </details>
              <div className={styles.investigationFooter}>
                <span>{artifactCountLabel(artifact.sourceCount)}</span>
                {artifact.model && <span>{artifact.model}</span>}
              </div>
            </article>
          ))}
        </div>
      )}
      {!isLoading && investigations?.onRefresh && <button className="button button--secondary" type="button" onClick={refresh}>Refresh investigations</button>}
    </section>
  );
}
