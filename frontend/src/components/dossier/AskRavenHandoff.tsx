import styles from "./dossier.module.css";

export interface AskRavenHandoffProps {
  companyId: string;
  companyName: string;
  profileVersion?: number | null;
  sourceCount: number;
  lastResearchedAt?: string | null;
}

function display(value?: number | string | null): string {
  if (value === null || value === undefined || value === "") return "Not available";
  return String(value);
}

function formatDate(value?: string | null): string {
  if (!value) return "Not available";
  const timestamp = Date.parse(value);
  return Number.isNaN(timestamp) ? "Not available" : new Intl.DateTimeFormat(undefined, { dateStyle: "medium" }).format(timestamp);
}

export function AskRavenHandoff({ companyId, companyName, profileVersion, sourceCount, lastResearchedAt }: AskRavenHandoffProps) {
  return (
    <section className={styles.handoff} data-testid="ask-raven-handoff" aria-labelledby="ask-raven-heading">
      <div className={styles.handoffHeader}><span aria-hidden="true" className={styles.aiMark}>AI</span><div><p className={styles.eyebrow}>Company-scoped handoff</p><h2 id="ask-raven-heading">Ask RAVEN</h2></div></div>
      <p>This is the attachment point for a future grounded conversation about <strong>{companyName}</strong>. Answers are not enabled in this workspace yet, so no placeholder chat or fabricated response is shown.</p>
      <dl className={styles.handoffContext} aria-label="Ask RAVEN context">
        <div><dt>Company ID</dt><dd>{display(companyId)}</dd></div>
        <div><dt>Profile version</dt><dd>{display(profileVersion)}</dd></div>
        <div><dt>Stored sources</dt><dd>{sourceCount}</dd></div>
        <div><dt>Last researched</dt><dd>{formatDate(lastResearchedAt)}</dd></div>
      </dl>
    </section>
  );
}
