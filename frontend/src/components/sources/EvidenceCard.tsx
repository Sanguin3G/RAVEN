import styles from "./sources.module.css";
import { displayDomain, formatRetrievedAt, safeExternalUrl, type SourceKind } from "./sourceUtils";
import { SourceBadge } from "./SourceBadge";
import { SourceIcon } from "./SourceIcon";

export type EvidenceStatus = "acquired" | "failed" | "duplicate";

export type EvidenceRecord = {
  id: string;
  url: string;
  title: string;
  domain?: string | null;
  kind?: SourceKind | null;
  iconUrl?: string | null;
  preview?: string | null;
  retrievedAt?: string | null;
  crawlerProvider?: string | null;
  status?: EvidenceStatus;
  statusMessage?: string | null;
};

export type EvidenceCardProps = {
  evidence: EvidenceRecord;
};

function statusLabel(status?: EvidenceStatus): string | undefined {
  switch (status) {
    case "acquired":
      return "Acquired evidence";
    case "failed":
      return "Acquisition failed";
    case "duplicate":
      return "Duplicate content skipped";
    default:
      return undefined;
  }
}
export function EvidenceCard({ evidence }: EvidenceCardProps) {
  const domain = displayDomain(evidence.domain, evidence.url);
  const safeUrl = safeExternalUrl(evidence.url);
  const retrievedAt = formatRetrievedAt(evidence.retrievedAt);
  const label = statusLabel(evidence.status);

  return (
    <article className={styles.evidenceCard} data-testid={`evidence-${evidence.id}`}>
      <header className={styles.evidenceHeader}>
        <SourceIcon domain={domain} iconUrl={evidence.iconUrl} kind={evidence.kind} provider={evidence.crawlerProvider} size="medium" />
        <span className={styles.evidenceHeading}>
          <span className={styles.evidenceBadges}>
            <SourceBadge kind={evidence.kind} />
            {label && <span className={`${styles.acquisitionStatus} ${styles[`acquisitionStatus${evidence.status![0].toUpperCase()}${evidence.status!.slice(1)}`]}`}>{label}</span>}
          </span>
          <strong>{evidence.title || domain || "Untitled source"}</strong>
          {domain && <span className={styles.sourceCardDomain}>{domain}</span>}
        </span>
      </header>
      {evidence.preview && <p className={styles.evidencePreview}>{evidence.preview}</p>}
      <dl className={styles.evidenceMeta}>
        {retrievedAt && (
          <div>
            <dt>Retrieved</dt>
            <dd>
              <time dateTime={evidence.retrievedAt ?? undefined}>{retrievedAt}</time>
            </dd>
          </div>
        )}
        {evidence.crawlerProvider && (
          <div>
            <dt>Provider</dt>
            <dd>{evidence.crawlerProvider}</dd>
          </div>
        )}
      </dl>
      {evidence.statusMessage && <p className={styles.evidenceStatusMessage}>{evidence.statusMessage}</p>}
      <footer className={styles.sourceCardFooter}>
        {safeUrl ? (
          <a href={safeUrl} rel="noreferrer noopener" target="_blank">
            Open source
          </a>
        ) : (
          <span className={styles.invalidUrl}>Source link unavailable</span>
        )}
      </footer>
    </article>
  );
}
