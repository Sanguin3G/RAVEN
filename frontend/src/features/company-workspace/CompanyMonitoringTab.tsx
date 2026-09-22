import { CompanyResearchTab } from "./CompanyResearchTab";
import type { DossierMonitoring, DossierResearch } from "./dossierTypes";

export interface CompanyMonitoringTabProps {
  companyName?: string;
  research?: DossierResearch | null;
  monitoring?: DossierMonitoring | null;
}

/**
 * Monitoring is the workspace home for scheduled/manual research activity.
 * The existing activity timeline remains reusable, but it is no longer exposed
 * as a misleading top-level "Research" dossier tab.
 */
export function CompanyMonitoringTab({ companyName, research, monitoring }: CompanyMonitoringTabProps) {
  return <CompanyResearchTab companyName={companyName} monitoring={monitoring} research={research} />;
}
