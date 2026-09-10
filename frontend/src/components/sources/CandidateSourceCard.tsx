import { useId } from "react";
import styles from "./sources.module.css";
import { displayDomain, safeExternalUrl, type SourceKind } from "./sourceUtils";
import { SourceBadge } from "./SourceBadge";
import { SourceIcon } from "./SourceIcon";

export type CandidateAcquisitionStatus = "idle" | "pending" | "acquired" | "failed" | "duplicate";

export type CandidateSource = {
  id: string;
  url: string;
  title: string;
  domain?: string | null;
  snippet?: string | null;
  kind?: SourceKind | null;
  iconUrl?: string | null;
  recommended?: boolean;
  recommendationReasons?: string[];
  selected: boolean;
  acquisitionStatus?: CandidateAcquisitionStatus;
  acquisitionMessage?: string | null;
};

export type CandidateSourceCardProps = {
  candidate: CandidateSource;
  onSelectionChange: (selected: boolean) => void;
  disabled?: boolean;
};

function acquisitionLabel(status?: CandidateAcquisitionStatus): string | undefined {
  switch (status) {
    case "pending":
      return "Acquiring source";
    case "acquired":
      return "Acquired";
    case "failed":
      return "Acquisition failed";
    case "duplicate":
      return "Duplicate content skipped";
    default:
      return undefined;
  }
}
export function CandidateSourceCard({ candidate, onSelectionChange, disabled = false }: CandidateSourceCardProps) {
  const inputId = useId();
  const reasonId = `${inputId}-reason`;
  const statusId = `${inputId}-status`;
  const safeUrl = safeExternalUrl(candidate.url);
  const domain = displayDomain(candidate.domain, candidate.url);
  const statusLabel = acquisitionLabel(candidate.acquisitionStatus);
  const statusDetail = candidate.acquisitionMessage || (candidate.acquisitionStatus === "duplicate" ? "An equivalent document was already stored." : undefined);

  return (
    <article className={`${styles.sourceCard} ${candidate.selected ? styles.sourceCardSelected : ""} ${disabled ? styles.sourceCardDisabled : ""}`} data-testid={`candidate-source-${candidate.id}`}>
      <label className={styles.candidateLabel} htmlFor={inputId}>
        <input
          aria-describedby={`${candidate.recommended && candidate.recommendationReasons?.length ? reasonId : ""} ${statusLabel ? statusId : ""}`.trim() || undefined}
          checked={candidate.selected}
          disabled={disabled}
          id={inputId}
          onChange={(event) => onSelectionChange(event.target.checked)}
          type="checkbox"
        />
        <span className={styles.checkboxVisual} aria-hidden="true" />
        <span className={styles.sourceCardBody}>
          <span className={styles.sourceCardTopline}>
            <SourceIcon domain={domain} iconUrl={candidate.iconUrl} kind={candidate.kind} size="medium" />
            <span className={styles.sourceCardClassification}>
              <span className={styles.sourceCardBadges}>
                <SourceBadge kind={candidate.kind} recommended={candidate.recommended} />
                {candidate.recommended && <span className={styles.recommendedLabel}>Recommended</span>}
              </span>
              <span className={styles.selectionState}>{candidate.selected ? "Selected" : "Not selected"}</span>
            </span>
          </span>
          <span className={styles.sourceCardTitle}>{candidate.title || domain || "Untitled source"}</span>
          {domain && <span className={styles.sourceCardDomain}>{domain}</span>}
          {candidate.snippet && <span className={styles.sourceCardSnippet}>{candidate.snippet}</span>}
          {candidate.recommended && candidate.recommendationReasons && candidate.recommendationReasons.length > 0 && (
            <span className={styles.recommendationReasons} id={reasonId}>
              {candidate.recommendationReasons.join(" · ")}
            </span>
          )}
        </span>
      </label>
      <span className={styles.sourceCardFooter}>
        {statusLabel && (
          <span className={`${styles.acquisitionStatus} ${styles[`acquisitionStatus${candidate.acquisitionStatus![0].toUpperCase()}${candidate.acquisitionStatus!.slice(1)}`]}`} id={statusId}>
            {statusLabel}{statusDetail ? ` — ${statusDetail}` : ""}
          </span>
        )}
        {safeUrl ? (
          <a href={safeUrl} rel="noreferrer noopener" target="_blank">
            Open source
          </a>
        ) : (
          <span className={styles.invalidUrl}>Source link unavailable</span>
        )}
      </span>
    </article>
  );
}
