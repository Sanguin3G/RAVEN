import { useEffect, useState } from "react";
import { getApiErrorMessage } from "../../api/client";
import { getSavedInvestigations, type SavedResearchArtifact } from "../../api/investigations";
import { getManagedResearchJobs, type ManagedResearchJob } from "../../api/managedResearch";
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
        <span>{artifacts.length + completedManagedJobs.length} available</span>
      </div>

      {isLoading && <p className={styles.contextNote} role="status">Loading saved investigations…</p>}
      {error && <p className={styles.errorMessage} role="alert">{error}</p>}
      {!isLoading && !error && !artifacts.length && !completedManagedJobs.length && (
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
      {artifacts.length > 0 && (
        <div className={styles.investigationList}>
          {artifacts.map((artifact) => (
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
