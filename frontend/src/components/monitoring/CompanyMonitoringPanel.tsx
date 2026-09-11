import { ArrowClockwise } from "@phosphor-icons/react/dist/csr/ArrowClockwise";
import { BellRinging } from "@phosphor-icons/react/dist/csr/BellRinging";
import { CalendarDots } from "@phosphor-icons/react/dist/csr/CalendarDots";
import { CheckCircle } from "@phosphor-icons/react/dist/csr/CheckCircle";
import { CircleNotch } from "@phosphor-icons/react/dist/csr/CircleNotch";
import { Info } from "@phosphor-icons/react/dist/csr/Info";
import { WarningCircle } from "@phosphor-icons/react/dist/csr/WarningCircle";
import { useEffect, useId, useState } from "react";
import type { CompanyMonitoring, MonitoringCadence, MonitoringRunStatus, UpdateCompanyMonitoring } from "../../api/monitoring";
import styles from "./monitoring.module.css";

export type { CompanyMonitoring, MonitoringCadence, MonitoringRunStatus, UpdateCompanyMonitoring } from "../../api/monitoring";

export interface CompanyMonitoringPanelProps {
  monitoring: CompanyMonitoring;
  companyName?: string;
  isLoading?: boolean;
  isSaving?: boolean;
  isResearching?: boolean;
  error?: string | null;
  onUpdate?: (update: UpdateCompanyMonitoring) => void | Promise<void>;
  onResearchNow?: () => void | Promise<void>;
}

const cadenceOptions: Array<{ value: MonitoringCadence; label: string; description: string }> = [
  { value: "Daily", label: "Daily", description: "Check this company every day" },
  { value: "Weekly", label: "Weekly", description: "Check this company once a week" },
  { value: "Monthly", label: "Monthly", description: "Check this company once a month" },
];

function formatDate(value: string | null | undefined) {
  if (!value) return "Not scheduled";

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return "Date unavailable";

  return new Intl.DateTimeFormat(undefined, {
    day: "numeric",
    month: "short",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
  }).format(parsed);
}

function statusDetails(status: MonitoringRunStatus | null | undefined) {
  switch (status) {
    case "Running":
      return { label: "Research in progress", tone: styles.statusRunning, icon: CircleNotch };
    case "ReadyForReview":
      return { label: "New research update ready for review", tone: styles.statusReady, icon: CheckCircle };
    case "Completed":
      return { label: "Research completed", tone: styles.statusCompleted, icon: CheckCircle };
    case "Failed":
      return { label: "Research failed", tone: styles.statusFailed, icon: WarningCircle };
    case "Cancelled":
      return { label: "Research cancelled", tone: styles.statusCancelled, icon: Info };
    default:
      return { label: "No research run yet", tone: styles.statusIdle, icon: Info };
  }
}

export function CompanyMonitoringPanel({
  monitoring,
  companyName,
  isLoading = false,
  isSaving = false,
  isResearching = false,
  error,
  onUpdate,
  onResearchNow,
}: CompanyMonitoringPanelProps) {
  const [draft, setDraft] = useState<UpdateCompanyMonitoring>({
    enabled: monitoring.enabled,
    cadence: monitoring.cadence,
  });
  const cadenceId = useId();
  const status = statusDetails(monitoring.lastRunStatus);
  const StatusIcon = status.icon;
  const hasChanges = draft.enabled !== monitoring.enabled || draft.cadence !== monitoring.cadence;

  useEffect(() => {
    setDraft({ enabled: monitoring.enabled, cadence: monitoring.cadence });
  }, [monitoring.enabled, monitoring.cadence]);

  function submit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!onUpdate || !hasChanges || isSaving) return;
    void onUpdate(draft);
  }

  return (
    <section className={styles.panel} aria-labelledby={`${cadenceId}-heading`} data-testid="company-monitoring-panel">
      <header className={styles.header}>
        <div className={styles.headingGroup}>
          <span className={styles.headingIcon} aria-hidden="true"><BellRinging size={20} weight="duotone" /></span>
          <div>
            <p className={styles.eyebrow}>Research monitoring</p>
            <h2 id={`${cadenceId}-heading`}>{companyName ? `Monitor ${companyName}` : "Monitor company"}</h2>
          </div>
        </div>
        <span className={`${styles.enabledBadge} ${monitoring.enabled ? styles.enabled : styles.disabled}`}>
          <span className={styles.badgeDot} aria-hidden="true" />
          {monitoring.enabled ? "On" : "Off"}
        </span>
      </header>

      {isLoading ? (
        <p className={styles.stateMessage} role="status">Loading monitoring settings…</p>
      ) : (
        <>
          <p className={styles.description}>
            RAVEN can re-check public sources on a schedule and hold any profile update for your review.
          </p>

          <form className={styles.form} onSubmit={submit}>
            <div className={styles.toggleRow}>
              <div>
                <label className={styles.toggleLabel} htmlFor={`${cadenceId}-enabled`}>Scheduled research</label>
                <p className={styles.fieldHint}>Create a review-ready update when the next check is due.</p>
              </div>
              <label className={styles.switch}>
                <span className={styles.srOnly}>Enable scheduled research</span>
                <input
                  id={`${cadenceId}-enabled`}
                  name="enabled"
                  type="checkbox"
                  checked={draft.enabled}
                  onChange={(event) => setDraft((current) => ({ ...current, enabled: event.target.checked }))}
                  disabled={!onUpdate || isSaving}
                />
                <span className={styles.switchTrack} aria-hidden="true"><span /></span>
              </label>
            </div>

            <div className={styles.cadenceField}>
              <label htmlFor={`${cadenceId}-cadence`}>Frequency</label>
              <select
                id={`${cadenceId}-cadence`}
                name="cadence"
                value={draft.cadence}
                onChange={(event) => setDraft((current) => ({ ...current, cadence: event.target.value as MonitoringCadence }))}
                disabled={!draft.enabled || !onUpdate || isSaving}
              >
                {cadenceOptions.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
              </select>
              <span className={styles.fieldHint}>{cadenceOptions.find((option) => option.value === draft.cadence)?.description}</span>
            </div>

            {onUpdate && (
              <div className={styles.formActions}>
                <button className="button button--secondary" type="submit" disabled={!hasChanges || isSaving} aria-busy={isSaving}>
                  {isSaving ? "Saving…" : "Save monitoring"}
                </button>
                {hasChanges && !isSaving && <span className={styles.unsaved} role="status">Unsaved changes</span>}
              </div>
            )}
          </form>

          <div className={styles.scheduleGrid} aria-label="Monitoring schedule">
            <div className={styles.scheduleItem}>
              <CalendarDots size={18} weight="duotone" aria-hidden="true" />
              <div><span>Next research</span><strong>{monitoring.enabled ? formatDate(monitoring.nextRunAt) : "Paused"}</strong></div>
            </div>
            <div className={styles.scheduleItem}>
              <ArrowClockwise size={18} weight="duotone" aria-hidden="true" />
              <div><span>Last result</span><strong>{monitoring.lastRunAt ? formatDate(monitoring.lastRunAt) : "No run yet"}</strong></div>
            </div>
          </div>

          <div className={`${styles.statusBanner} ${status.tone}`} role={monitoring.lastRunStatus === "Failed" ? "alert" : "status"}>
            <StatusIcon className={monitoring.lastRunStatus === "Running" ? styles.spin : undefined} size={18} weight="fill" aria-hidden="true" />
            <div><strong>{status.label}</strong>{monitoring.lastRunStatus && monitoring.lastRunAt && <span>Last checked {formatDate(monitoring.lastRunAt)}</span>}</div>
          </div>

          {monitoring.lastRunStatus === "ReadyForReview" && (
            <div className={styles.reviewReady} data-testid="monitoring-review-ready">
              <CheckCircle size={19} weight="fill" aria-hidden="true" />
              <p><strong>New research update available.</strong> Review the evidence and confirm it before it becomes the next accepted profile.</p>
            </div>
          )}

          {error && <p className={styles.error} role="alert">{error}</p>}

          <div className={styles.researchAction}>
            <div><strong>Want to check now?</strong><span>Start a manual research run without changing this schedule.</span></div>
            <button
              className="button"
              type="button"
              onClick={() => void onResearchNow?.()}
              disabled={!onResearchNow || isResearching}
              aria-busy={isResearching}
              title={!onResearchNow ? "Manual research is unavailable in this view" : undefined}
            >
              <ArrowClockwise size={16} weight="bold" aria-hidden="true" />
              {isResearching ? "Researching…" : "Research now"}
            </button>
          </div>
        </>
      )}
    </section>
  );
}
