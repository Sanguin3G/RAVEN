import { ArrowDown } from "@phosphor-icons/react/dist/csr/ArrowDown";
import { ArrowUp } from "@phosphor-icons/react/dist/csr/ArrowUp";
import { ArrowsClockwise } from "@phosphor-icons/react/dist/csr/ArrowsClockwise";
import { GitDiff } from "@phosphor-icons/react/dist/csr/GitDiff";
import type { CompanyProfileVersion } from "../../types/profile";
import type { ProfileChange } from "../../api/profileTracking";
import styles from "./profileTracking.module.css";

export interface ProfileChangesPanelProps {
  changes: ProfileChange[];
  versions?: CompanyProfileVersion[];
  isLoading?: boolean;
  error?: string | null;
  fromVersion?: number | null;
  toVersion?: number | null;
}

function formatDate(value: string | null | undefined) {
  if (!value) return "Date unavailable";

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return "Date unavailable";

  return new Intl.DateTimeFormat(undefined, {
    day: "numeric",
    month: "short",
    year: "numeric",
  }).format(parsed);
}

function titleFromPath(fieldPath: string, itemKey?: string | null) {
  const cleanPath = fieldPath.replace(/^\$\.?/, "").replaceAll(".", " › ");
  const title = cleanPath || "Profile field";
  return itemKey ? `${title} · ${itemKey}` : title;
}

function readValue(value: string | null | undefined) {
  if (value === null || value === undefined || value.trim() === "") return "Not available";

  try {
    const parsed: unknown = JSON.parse(value);
    if (parsed === null || parsed === undefined) return "Not available";
    if (typeof parsed === "string") return parsed || "Not available";
    if (typeof parsed === "number" || typeof parsed === "boolean") return String(parsed);
    if (Array.isArray(parsed) && parsed.length === 0) return "None";
    if (Array.isArray(parsed)) return parsed.map((item) => formatStructuredValue(item)).join(", ");
    return formatStructuredValue(parsed);
  } catch {
    return "Value unavailable";
  }
}

function formatStructuredValue(value: unknown): string {
  if (typeof value === "string") return value;
  if (value === null || value === undefined) return "Not available";
  if (typeof value === "number" || typeof value === "boolean") return String(value);

  try {
    return JSON.stringify(value);
  } catch {
    return "Value unavailable";
  }
}

function changeIcon(changeType: ProfileChange["changeType"]) {
  if (changeType === "Added") return <ArrowUp size={16} weight="bold" aria-hidden="true" />;
  if (changeType === "Removed") return <ArrowDown size={16} weight="bold" aria-hidden="true" />;
  return <ArrowsClockwise size={16} weight="bold" aria-hidden="true" />;
}

export function ProfileChangesPanel({
  changes,
  versions = [],
  isLoading = false,
  error,
  fromVersion,
  toVersion,
}: ProfileChangesPanelProps) {
  const orderedChanges = [...changes].sort((left, right) => {
    const dateDifference = new Date(right.detectedAt).getTime() - new Date(left.detectedAt).getTime();
    return Number.isNaN(dateDifference) ? 0 : dateDifference;
  });
  const inferredLatestVersion = Math.max(
    ...versions.map((version) => version.version),
    ...changes.map((change) => change.toVersion),
    0,
  );
  const latestVersion = toVersion ?? (inferredLatestVersion || null);
  const previousVersion = fromVersion ?? (latestVersion && latestVersion > 1 ? latestVersion - 1 : null);
  const latest = versions.find((version) => version.version === latestVersion);
  const previous = versions.find((version) => version.version === previousVersion);
  const relevantChanges = latestVersion === null
    ? orderedChanges
    : orderedChanges.filter((change) => change.toVersion === latestVersion && (previousVersion === null || change.fromVersion === previousVersion));

  return (
    <section className={styles.panel} aria-labelledby="profile-changes-heading" data-testid="profile-changes-panel">
      <header className={styles.panelHeader}>
        <div className={styles.headingGroup}>
          <span className={`${styles.headingIcon} ${styles.headingIconChanges}`} aria-hidden="true">
            <GitDiff size={19} weight="bold" />
          </span>
          <div>
            <p className={styles.eyebrow}>Change tracking</p>
            <h2 id="profile-changes-heading">
              {previousVersion ? `Changes from v${previousVersion} to v${latestVersion}` : "Profile changes"}
            </h2>
          </div>
        </div>
        {latest && previous && (
          <span className={styles.changeDate}>
            {formatDate(previous.confirmedAt)} <span aria-hidden="true">→</span> {formatDate(latest.confirmedAt)}
          </span>
        )}
      </header>

      {isLoading && <p className={styles.stateMessage} role="status">Loading profile changes…</p>}
      {error && !isLoading && <p className={`${styles.stateMessage} ${styles.stateError}`} role="alert">{error}</p>}

      {!isLoading && !error && versions.length > 0 && previousVersion === null && (
        <div className={styles.emptyState} data-testid="profile-changes-first-version">
          <GitDiff size={23} weight="duotone" aria-hidden="true" />
          <div>
            <strong>This is the first accepted profile</strong>
            <p>RAVEN will show additions, removals, and updates after the next confirmed refresh.</p>
          </div>
        </div>
      )}

      {!isLoading && !error && previousVersion !== null && relevantChanges.length === 0 && (
        <div className={styles.emptyState} data-testid="profile-changes-none">
          <GitDiff size={23} weight="duotone" aria-hidden="true" />
          <div>
            <strong>No changes detected</strong>
            <p>v{latestVersion} contains the same tracked facts as v{previousVersion}.</p>
          </div>
        </div>
      )}

      {!isLoading && !error && relevantChanges.length > 0 && (
        <ul className={styles.changeList} aria-label="Profile changes">
          {relevantChanges.map((change) => (
            <li key={change.id} className={`${styles.changeItem} ${styles[`change${change.changeType}`]}`}>
              <span className={styles.changeMarker} aria-hidden="true">{changeIcon(change.changeType)}</span>
              <span className={styles.changeBody}>
                <strong>{titleFromPath(change.fieldPath, change.itemKey)}</strong>
                <span className={styles.changeType}>{change.changeType}</span>
                {change.changeType === "Changed" ? (
                  <span className={styles.valueTransition}>
                    <span className={styles.oldValue}>{readValue(change.oldValueJson)}</span>
                    <span className={styles.transitionArrow} aria-hidden="true">→</span>
                    <span className={styles.newValue}>{readValue(change.newValueJson)}</span>
                  </span>
                ) : (
                  <span className={change.changeType === "Added" ? styles.newValue : styles.oldValue}>
                    {readValue(change.changeType === "Added" ? change.newValueJson : change.oldValueJson)}
                  </span>
                )}
                <time className={styles.changeDetectedAt} dateTime={change.detectedAt}>Detected {formatDate(change.detectedAt)}</time>
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
