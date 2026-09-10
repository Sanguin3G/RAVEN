import styles from "./sources.module.css";

export type ResearchStage =
  | "idle"
  | "identifying"
  | "discovering"
  | "awaitingSourceSelection"
  | "acquiring"
  | "evidenceReady"
  | "generatingProfile"
  | "awaitingProfileConfirmation"
  | "completed"
  | "failed";

export type ResearchActivityStatus = "completed" | "active" | "waiting" | "failed" | "pending";

export type ResearchActivityItem = {
  id: string;
  label: string;
  status: ResearchActivityStatus;
  detail?: string | null;
};

export type ResearchActivityCounters = {
  queriesTotal?: number;
  queriesCompleted?: number;
  searchResultsFound?: number;
  uniqueCandidates?: number;
  recommendedCandidates?: number;
  sourcesSelected?: number;
  crawlTotal?: number;
  crawlCompleted?: number;
  crawlSucceeded?: number;
  crawlFailed?: number;
  documentsAdded?: number;
  duplicatesSkipped?: number;
};

export type ResearchActivityProps = {
  stage: ResearchStage;
  items?: ResearchActivityItem[];
  counters?: ResearchActivityCounters;
  failureMessage?: string | null;
  className?: string;
};

const stageItems: Array<{ stage: Exclude<ResearchStage, "idle" | "failed">; label: string }> = [
  { stage: "identifying", label: "Identity prepared" },
  { stage: "discovering", label: "Discover public sources" },
  { stage: "awaitingSourceSelection", label: "Review candidate sources" },
  { stage: "acquiring", label: "Acquire selected sources" },
  { stage: "evidenceReady", label: "Review acquired evidence" },
  { stage: "generatingProfile", label: "Generate company profile" },
  { stage: "awaitingProfileConfirmation", label: "Confirm profile" },
  { stage: "completed", label: "Research complete" },
];

const counterLabels: Array<[keyof ResearchActivityCounters, string]> = [
  ["queriesTotal", "Brave queries planned"],
  ["queriesCompleted", "Brave queries"],
  ["searchResultsFound", "Search results"],
  ["uniqueCandidates", "Unique candidates"],
  ["recommendedCandidates", "Recommended sources"],
  ["sourcesSelected", "Sources selected"],
  ["crawlTotal", "Crawls planned"],
  ["crawlCompleted", "Crawls completed"],
  ["crawlSucceeded", "Crawls succeeded"],
  ["crawlFailed", "Crawls failed"],
  ["documentsAdded", "Documents added"],
  ["duplicatesSkipped", "Duplicates skipped"],
];

function stageLabel(stage: ResearchStage): string {
  if (stage === "failed") return "Research failed";
  return stageItems.find((item) => item.stage === stage)?.label || "Ready to research";
}

function derivedItems(stage: ResearchStage): ResearchActivityItem[] {
  if (stage === "idle") return [];
  if (stage === "failed") {
    return [{ id: "failed", label: "Research failed", status: "failed" }];
  }

  const currentIndex = stageItems.findIndex((item) => item.stage === stage);
  return stageItems.map((item, index) => ({
    id: item.stage,
    label: item.label,
    status: index < currentIndex ? "completed" : index === currentIndex ? (stage === "completed" ? "completed" : "active") : "pending",
  }));
}

function formatCounter(key: keyof ResearchActivityCounters, counters: ResearchActivityCounters): string | undefined {
  const value = counters[key];
  if (value === undefined) return undefined;

  if (key === "queriesCompleted" && counters.queriesTotal !== undefined) return `${value} / ${counters.queriesTotal}`;
  if (key === "crawlCompleted" && counters.crawlTotal !== undefined) return `${value} / ${counters.crawlTotal}`;
  return String(value);
}

export function ResearchActivity({ stage, items, counters, failureMessage, className = "" }: ResearchActivityProps) {
  const activityItems = items || derivedItems(stage);
  const visibleCounters = counters
    ? counterLabels
        .map(([key, label]) => ({ key, label, value: formatCounter(key, counters) }))
        .filter((counter): counter is { key: keyof ResearchActivityCounters; label: string; value: string } => counter.value !== undefined)
    : [];
  const statusText = stage === "failed" ? `Research failed${failureMessage ? `: ${failureMessage}` : ""}` : stage === "completed" ? "Research completed" : stage === "awaitingSourceSelection" || stage === "awaitingProfileConfirmation" ? `Waiting for you: ${stageLabel(stage)}` : `Research activity: ${stageLabel(stage)}`;

  return (
    <section aria-labelledby="research-activity-heading" className={`${styles.activity} ${className}`}>
      <div className={styles.activityHeader}>
        <div>
          <p className={styles.activityEyebrow}>Research activity</p>
          <h2 id="research-activity-heading">{stageLabel(stage)}</h2>
        </div>
        <p aria-live="polite" className={styles.activityStatus} role="status">
          {statusText}
        </p>
      </div>
      {failureMessage && stage === "failed" && <p className={styles.activityFailure}>{failureMessage}</p>}
      {activityItems.length > 0 && (
        <ol className={styles.activityList}>
          {activityItems.map((item) => (
            <li className={`${styles.activityItem} ${styles[`activityItem${item.status[0].toUpperCase()}${item.status.slice(1)}`]}`} key={item.id}>
              <span aria-hidden="true" className={styles.activityMarker} />
              <span className={styles.activityItemCopy}>
                <strong>{item.label}</strong>
                {item.detail && <span>{item.detail}</span>}
              </span>
            </li>
          ))}
        </ol>
      )}
      {visibleCounters.length > 0 && (
        <dl className={styles.activityCounters}>
          {visibleCounters.map((counter) => (
            <div key={counter.key}>
              <dt>{counter.label}</dt>
              <dd>{counter.value}</dd>
            </div>
          ))}
        </dl>
      )}
    </section>
  );
}
