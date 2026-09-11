import styles from "./dossier.module.css";
import { ProfileChangesPanel, ProfileHistoryPanel } from "../profile-tracking";
import type { DossierTracking } from "./dossierTypes";

export interface CompanyChangesTabProps {
  tracking?: DossierTracking | null;
}

export function CompanyChangesTab({ tracking }: CompanyChangesTabProps) {
  if (!tracking) {
    return <div className={`${styles.emptyState} ${styles.unavailable}`} data-testid="dossier-changes"><h2>Loading change history</h2><p>RAVEN is preparing immutable profile versions and their persisted comparison.</p></div>;
  }

  const currentVersion = tracking.versions[0]?.version;
  return (
    <div className={styles.trackingStack} data-testid="dossier-changes">
      <ProfileHistoryPanel
        versions={tracking.versions}
        currentVersion={currentVersion}
        isLoading={tracking.isLoading}
        error={tracking.error}
        isRefreshing={tracking.isRefreshing}
        onRefreshResearch={tracking.onRefreshResearch}
      />
      <ProfileChangesPanel
        changes={tracking.changes}
        versions={tracking.versions}
        isLoading={tracking.isLoading}
        error={tracking.error}
      />
    </div>
  );
}
