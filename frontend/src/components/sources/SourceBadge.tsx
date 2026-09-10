import styles from "./sources.module.css";
import { sourceKindLabel, type SourceKind } from "./sourceUtils";

export type SourceBadgeProps = {
  kind?: SourceKind | null;
  recommended?: boolean;
  children?: string;
};

export function SourceBadge({ kind, recommended = false, children }: SourceBadgeProps) {
  return (
    <span className={`${styles.sourceBadge} ${recommended ? styles.sourceBadgeRecommended : ""}`} data-source-kind={kind ?? "unknown"}>
      {children || sourceKindLabel(kind)}
    </span>
  );
}
