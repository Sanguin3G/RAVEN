import styles from "./ask-raven-next-action-review.module.css";

export type AskRavenNextActionKind = "latest" | "research";

interface Props {
  kind: AskRavenNextActionKind;
  value: string;
  busy: boolean;
  onChange: (value: string) => void;
  onCancel: () => void;
  onConfirm: () => void;
}

export function AskRavenNextActionReview({ kind, value, busy, onChange, onCancel, onConfirm }: Props) {
  const isLatest = kind === "latest";
  return <section className={styles.review} aria-label={isLatest ? "Search latest review" : "Research further review"}>
    <strong>{isLatest ? "Search latest" : "Research further"}</strong>
    <p>{isLatest ? "Search current public information related to this answer." : "Investigate in depth:"}</p>
    <label className="sr-only" htmlFor={`next-action-${kind}`}>{isLatest ? "Follow-up question" : "Research objective"}</label>
    <textarea id={`next-action-${kind}`} value={value} maxLength={4_000} rows={2} onChange={(event) => onChange(event.target.value)} />
    {isLatest ? <small>Web Search will be used for this follow-up.</small> : <small>Depth: Adaptive</small>}
    <div className={styles.actions}>
      <button type="button" onClick={onCancel} disabled={busy}>Cancel</button>
      <button type="button" onClick={onConfirm} disabled={busy || !value.trim()}>{busy ? "Starting…" : isLatest ? "Search" : "Start research"}</button>
    </div>
  </section>;
}
