import { ArrowClockwise } from "@phosphor-icons/react/dist/csr/ArrowClockwise";
import { CheckCircle } from "@phosphor-icons/react/dist/csr/CheckCircle";
import { ClockCounterClockwise } from "@phosphor-icons/react/dist/csr/ClockCounterClockwise";
import { GitBranch } from "@phosphor-icons/react/dist/csr/GitBranch";
import type { CompanyProfileVersion } from "../../types/profile";
import styles from "./profileTracking.module.css";

export interface ProfileHistoryPanelProps {
  versions: CompanyProfileVersion[];
  currentVersion?: number | null;
  isLoading?: boolean;
  error?: string | null;
  isRefreshing?: boolean;
  onRefreshResearch?: () => void | Promise<void>;
  onSelectVersion?: (version: CompanyProfileVersion) => void;
}

function formatDate(value: string | null | undefined) {
  if (!value) return "Date unavailable";

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

function versionLabel(version: CompanyProfileVersion, currentVersion?: number | null) {
  return currentVersion === version.version || (!currentVersion && version.version === 1) ? "Current" : undefined;
}

export function ProfileHistoryPanel({
  versions,
  currentVersion,
  isLoading = false,
  error,
  isRefreshing = false,
  onRefreshResearch,
  onSelectVersion,
}: ProfileHistoryPanelProps) {
  const orderedVersions = [...versions].sort((left, right) => right.version - left.version);
  const resolvedCurrentVersion = currentVersion ?? orderedVersions[0]?.version ?? null;

  return (
    <section className={styles.panel} aria-labelledby="profile-history-heading" data-testid="profile-history-panel">
      <header className={styles.panelHeader}>
        <div className={styles.headingGroup}>
          <span className={`${styles.headingIcon} ${styles.headingIconHistory}`} aria-hidden="true">
            <ClockCounterClockwise size={19} weight="bold" />
          </span>
          <div>
            <p className={styles.eyebrow}>Profile history</p>
            <h2 id="profile-history-heading">Accepted profile versions</h2>
          </div>
        </div>
        {onRefreshResearch && (
          <button
            type="button"
            className={styles.refreshButton}
            onClick={() => void onRefreshResearch()}
            disabled={isRefreshing}
            aria-busy={isRefreshing}
          >
            <ArrowClockwise size={16} weight="bold" aria-hidden="true" />
            {isRefreshing ? "Refreshing…" : "Refresh research"}
          </button>
        )}
      </header>

      {isLoading && <p className={styles.stateMessage} role="status">Loading profile history…</p>}
      {error && !isLoading && <p className={`${styles.stateMessage} ${styles.stateError}`} role="alert">{error}</p>}

      {!isLoading && !error && orderedVersions.length === 0 && (
        <div className={styles.emptyState} data-testid="profile-history-empty">
          <GitBranch size={23} weight="duotone" aria-hidden="true" />
          <div>
            <strong>No accepted profile versions yet</strong>
            <p>Run research and confirm the generated profile to start this company’s history.</p>
          </div>
        </div>
      )}

      {!isLoading && !error && orderedVersions.length > 0 && (
        <ol className={styles.versionList} aria-label="Accepted profile versions">
          {orderedVersions.map((version) => {
            const label = versionLabel(version, resolvedCurrentVersion);
            const selectable = Boolean(onSelectVersion);
            const content = (
              <>
                <span className={styles.versionMarker} aria-hidden="true">
                  {label ? <CheckCircle size={16} weight="fill" /> : <span />}
                </span>
                <span className={styles.versionCopy}>
                  <span className={styles.versionTitle}>
                    <strong>v{version.version}</strong>
                    {label && <span className={styles.currentBadge}>{label}</span>}
                  </span>
                  <span className={styles.versionDate}>
                    Confirmed {formatDate(version.confirmedAt)}
                  </span>
                  <span className={styles.versionMeta}>
                    {version.evidence.length} evidence {version.evidence.length === 1 ? "reference" : "references"}
                    {version.aiModel ? ` · ${version.aiModel}` : ""}
                  </span>
                </span>
                {selectable && <span className={styles.versionAction} aria-hidden="true">View</span>}
              </>
            );

            return (
              <li key={version.id || version.version} className={`${styles.versionItem} ${selectable ? styles.versionItemSelectable : ""}`}>
                {selectable ? (
                  <button type="button" className={styles.versionButton} onClick={() => onSelectVersion?.(version)}>
                    {content}
                  </button>
                ) : (
                  <div className={styles.versionButton}>{content}</div>
                )}
              </li>
            );
          })}
        </ol>
      )}
    </section>
  );
}
