import styles from "./dossier.module.css";

export function CompanyChangesTab() {
  return <div className={`${styles.emptyState} ${styles.unavailable}`} data-testid="dossier-changes"><h2>Change history is not available yet</h2><p>RAVEN preserves profile versions for future comparison, but a change timeline will be added when refresh diffing is implemented.</p></div>;
}
