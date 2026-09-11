import { useId, useRef, useState, type KeyboardEvent } from "react";
import styles from "./dossier.module.css";
import { AskRavenHandoff } from "./AskRavenHandoff";
import { CompanyChangesTab } from "./CompanyChangesTab";
import { CompanyIdentityHeader } from "./CompanyIdentityHeader";
import { CompanyOverview } from "./CompanyOverview";
import { CompanyResearchTab } from "./CompanyResearchTab";
import { CompanySourcesTab } from "./CompanySourcesTab";
import type { CompanyDossierProps, DossierTab } from "./dossierTypes";

const tabs: Array<{ id: DossierTab; label: string }> = [
  { id: "overview", label: "Overview" },
  { id: "sources", label: "Sources" },
  { id: "research", label: "Research" },
  { id: "changes", label: "Changes" },
  { id: "ask", label: "Ask RAVEN" },
];

export function CompanyDossier({ company, profile, sources, research, tracking, monitoring, activeTab, initialTab = "overview", onTabChange }: CompanyDossierProps) {
  const [internalTab, setInternalTab] = useState<DossierTab>(initialTab);
  const tabRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const idPrefix = useId();
  const selectedTab = activeTab ?? internalTab;

  const selectTab = (tab: DossierTab) => {
    if (activeTab === undefined) setInternalTab(tab);
    onTabChange?.(tab);
  };

  const onTabKeyDown = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    let nextIndex: number | undefined;
    if (event.key === "ArrowRight") nextIndex = (index + 1) % tabs.length;
    if (event.key === "ArrowLeft") nextIndex = (index - 1 + tabs.length) % tabs.length;
    if (event.key === "Home") nextIndex = 0;
    if (event.key === "End") nextIndex = tabs.length - 1;
    if (nextIndex === undefined) return;
    event.preventDefault();
    const next = tabs[nextIndex];
    selectTab(next.id);
    tabRefs.current[nextIndex]?.focus();
  };

  const tabPanelId = `${idPrefix}-panel-${selectedTab}`;

  return (
    <div className={styles.workspace} data-testid="company-dossier">
      <CompanyIdentityHeader company={company} profile={profile} />
      <div className={styles.tabList} role="tablist" aria-label={`${company.displayName} dossier sections`}>
        {tabs.map((tab, index) => {
          const tabId = `${idPrefix}-tab-${tab.id}`;
          const selected = selectedTab === tab.id;
          return <button aria-controls={tabPanelId} aria-selected={selected} className={`${styles.tab} ${selected ? styles.tabActive : ""}`} id={tabId} key={tab.id} onClick={() => selectTab(tab.id)} onKeyDown={(event) => onTabKeyDown(event, index)} ref={(element) => { tabRefs.current[index] = element; }} role="tab" tabIndex={selected ? 0 : -1} type="button">{tab.label}</button>;
        })}
      </div>
      <div aria-labelledby={`${idPrefix}-tab-${selectedTab}`} className={styles.tabPanel} id={tabPanelId} role="tabpanel" tabIndex={0}>
        {selectedTab === "overview" && <CompanyOverview company={company} profile={profile} />}
        {selectedTab === "sources" && <CompanySourcesTab sources={sources} />}
        {selectedTab === "research" && <CompanyResearchTab companyName={company.displayName} monitoring={monitoring} research={research} />}
        {selectedTab === "changes" && <CompanyChangesTab tracking={tracking} />}
        {selectedTab === "ask" && <AskRavenHandoff companyId={company.id} companyName={company.displayName} lastResearchedAt={company.lastResearchedAt} profileVersion={profile?.version} sourceCount={sources?.length ?? profile?.evidenceCount ?? 0} />}
      </div>
    </div>
  );
}
