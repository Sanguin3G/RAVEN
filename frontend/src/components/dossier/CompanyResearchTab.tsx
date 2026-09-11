import { CompanyMonitoringPanel } from "../monitoring";
import styles from "./dossier.module.css";
import type { DossierMonitoring, DossierResearch, DossierResearchStepState } from "./dossierTypes";

export interface CompanyResearchTabProps {
  research?: DossierResearch | null;
  monitoring?: DossierMonitoring | null;
  companyName?: string;
}

const statusLabels = {
  idle: "Idle",
  working: "Working",
  waiting: "Waiting for user",
  failed: "Failed",
  completed: "Completed",
} as const;

function stepClass(state: DossierResearchStepState): string {
  return `${styles.step} ${styles[`step${state[0].toUpperCase()}${state.slice(1)}`]}`;
}

export function CompanyResearchTab({ research, monitoring, companyName }: CompanyResearchTabProps) {
  if (!research && !monitoring) {
    return <div className={`${styles.emptyState} ${styles.unavailable}`} data-testid="dossier-research-empty"><h2>No research run selected</h2><p>Research activity, provider status, and real counters will appear here when a run is available.</p></div>;
  }

  if (!research) {
    return <div className={styles.researchStack}><CompanyMonitoringPanel companyName={companyName} {...monitoring!} /></div>;
  }

  const steps = research.steps ?? [];
  const counters = research.counters ?? [];
  const statusClass = `${styles.timelineStatus} ${styles[`timelineStatus${research.status[0].toUpperCase()}${research.status.slice(1)}`]}`;

  return (
    <div className={styles.researchStack}>
      <section className={`${styles.section} ${styles.timeline}`} data-testid="dossier-research" aria-labelledby="dossier-research-heading" aria-live="polite">
        <div className={styles.timelineHeader}>
          <div><h2 id="dossier-research-heading">Research activity</h2><p>{research.stageLabel || research.summary || "Current run state"}</p></div>
          <span className={statusClass}>{statusLabels[research.status]}</span>
        </div>
        {research.error && <p className={styles.errorMessage} role="alert">{research.error}</p>}
        {steps.length ? <ol className={styles.steps}>{steps.map((step) => <li className={stepClass(step.state)} key={step.id}><span className={styles.stepMarker} aria-hidden="true" /><span className={styles.stepCopy}><strong>{step.label}</strong>{step.detail && <span>{step.detail}</span>}</span></li>)}</ol> : <p className={styles.contextNote}>No staged activity has been recorded for this run.</p>}
        {counters.length ? <dl className={styles.counters}>{counters.map((counter) => <div key={counter.label}><dt>{counter.label}</dt><dd>{counter.value}</dd></div>)}</dl> : null}
        {(research.provider || research.model || research.runId) && <p className={styles.contextNote}>{research.provider && `Provider: ${research.provider}`}{research.provider && research.model ? " · " : ""}{research.model && `Model: ${research.model}`}{(research.provider || research.model) && research.runId ? " · " : ""}{research.runId && `Run ${research.runId}`}</p>}
      </section>
      {monitoring && <CompanyMonitoringPanel companyName={companyName} {...monitoring} />}
    </div>
  );
}
