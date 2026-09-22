import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import { ChatCircleDots, Sparkle } from "@phosphor-icons/react";
import styles from "./company-workspace.module.css";
import { CompanyChangesTab } from "./CompanyChangesTab";
import { AskRavenHandoff } from "../ask-raven/AskRavenHandoff";
import { CompanyIdentityHeader } from "./CompanyIdentityHeader";
import { CompanyInvestigationsTab } from "./CompanyInvestigationsTab";
import { CompanyMonitoringTab } from "./CompanyMonitoringTab";
import { CompanyOverview } from "./CompanyOverview";
import { CompanySourcesTab } from "./CompanySourcesTab";
import { ExternalResearchAssistModal } from "./ExternalResearchAssistModal";
import { TargetedEnrichmentPanel } from "./TargetedEnrichmentPanel";
import type { ResearchTarget } from "../../api/coverage";
import type { CompanyDossierProps, DossierTab } from "./dossierTypes";
import { readProfileImprovementMaterialIds, rememberProfileImprovementMaterial } from "../../utils/profileImprovementState";

const tabs: Array<{ id: DossierTab; label: string }> = [
  { id: "overview", label: "Overview" },
  { id: "sources", label: "Sources" },
  { id: "investigations", label: "Investigations" },
  { id: "changes", label: "Changes" },
  { id: "monitoring", label: "Monitoring" },
];

export function CompanyDossier({ company, profile, sources, research, tracking, monitoring, coverage, investigations, initialEnrichmentTargets, initialEnrichmentArtifactId, initialManagedResearchInvestigationId, initialChatCapability, initialChatQuestion, openEnrichment, onProfileConfirmed, onOpenProfileImprovement, activeTab, initialTab = "overview", onTabChange }: CompanyDossierProps) {
  const [internalTab, setInternalTab] = useState<DossierTab>(initialTab);
  const [assistantCollapsed, setAssistantCollapsed] = useState(false);
  const [externalAssist, setExternalAssist] = useState<{ targets?: ResearchTarget[]; objective?: string } | null>(null);
  const [materialReview, setMaterialReview] = useState<{ targets: ResearchTarget[]; id: string; kind: "saved" | "managed" } | null>(null);
  const [profileImprovedMaterialIds, setProfileImprovedMaterialIds] = useState<Set<string>>(() => readProfileImprovementMaterialIds(company.id));
  const [chatLaunch, setChatLaunch] = useState<{ capability?: "deepResearch"; question?: string | null }>({ capability: initialChatCapability, question: initialChatQuestion });
  const tabRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const idPrefix = useId();
  const selectedTab = activeTab ?? internalTab;

  useEffect(() => {
    setChatLaunch({ capability: initialChatCapability, question: initialChatQuestion });
  }, [initialChatCapability, initialChatQuestion]);

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
    <div className={`${styles.workspaceShell} ${assistantCollapsed ? styles.workspaceShellCollapsed : ""}`} data-testid="company-dossier">
    <div className={styles.workspace}>
      <CompanyIdentityHeader company={company} profile={profile} />
      <div className={styles.tabList} role="tablist" aria-label={`${company.displayName} dossier sections`}>
        {tabs.map((tab, index) => {
          const tabId = `${idPrefix}-tab-${tab.id}`;
          const selected = selectedTab === tab.id;
          return <button aria-controls={`${idPrefix}-panel-${tab.id}`} aria-selected={selected} className={`${styles.tab} ${selected ? styles.tabActive : ""}`} id={tabId} key={tab.id} onClick={() => selectTab(tab.id)} onKeyDown={(event) => onTabKeyDown(event, index)} ref={(element) => { tabRefs.current[index] = element; }} role="tab" tabIndex={selected ? 0 : -1} type="button">{tab.label}</button>;
        })}
      </div>
      <div aria-labelledby={`${idPrefix}-tab-${selectedTab}`} className={styles.tabPanel} id={tabPanelId} role="tabpanel" tabIndex={0}>
        {selectedTab === "overview" && <CompanyOverview company={company} profile={profile} coverage={coverage} profileImprovedMaterialIds={profileImprovedMaterialIds} initialEnrichmentTargets={initialEnrichmentTargets} initialEnrichmentArtifactId={initialEnrichmentArtifactId} initialManagedResearchInvestigationId={initialManagedResearchInvestigationId} openEnrichment={openEnrichment} onProfileConfirmed={onProfileConfirmed} onOpenExternalResearch={(targets) => setExternalAssist({ targets })} />}
        {selectedTab === "sources" && <CompanySourcesTab sources={sources} />}
        {selectedTab === "investigations" && <CompanyInvestigationsTab companyId={company.id} companyName={company.displayName} profile={profile} investigations={investigations} profileImprovedMaterialIds={profileImprovedMaterialIds} onOpenExternalResearch={(objective) => setExternalAssist({ objective })} onOpenDeepResearch={(objective) => { setAssistantCollapsed(false); setChatLaunch({ capability: "deepResearch", question: objective || null }); }} onImproveProfile={(targets, sourceMaterialId, sourceMaterialKind) => {
          if (sourceMaterialId && sourceMaterialKind) {
            setMaterialReview({ targets, id: sourceMaterialId, kind: sourceMaterialKind });
            return;
          }
          onOpenProfileImprovement?.(targets, sourceMaterialId, sourceMaterialKind);
        }} />}
        {selectedTab === "changes" && <CompanyChangesTab tracking={tracking} />}
        {selectedTab === "monitoring" && <CompanyMonitoringTab companyName={company.displayName} monitoring={monitoring} research={research} />}
      </div>
    </div>
    <aside className={styles.assistantDock} aria-label="Ask RAVEN assistant">
      <button className={styles.assistantToggle} type="button" onClick={() => setAssistantCollapsed((value) => !value)} aria-expanded={!assistantCollapsed}>
        {assistantCollapsed ? <ChatCircleDots size={18} weight="fill" aria-hidden="true" /> : <Sparkle size={17} weight="fill" aria-hidden="true" />}
        <span>{assistantCollapsed ? "Ask RAVEN" : "Collapse assistant"}</span>
      </button>
      {!assistantCollapsed && <AskRavenHandoff companyId={company.id} companyName={company.displayName} lastResearchedAt={company.lastResearchedAt} profileVersion={profile?.version} profileVersionId={profile?.id} sourceCount={sources?.length ?? profile?.evidenceCount ?? 0} initialCapability={chatLaunch.capability} initialQuestion={chatLaunch.question} />}
    </aside>
    {materialReview ? <TargetedEnrichmentPanel company={company} profile={profile} initialTargets={materialReview.targets} initialMaterialArtifactId={materialReview.kind === "saved" ? materialReview.id : null} initialManagedResearchInvestigationId={materialReview.kind === "managed" ? materialReview.id : null} open onClose={() => setMaterialReview(null)} onConfirmed={(nextProfile) => { rememberProfileImprovementMaterial(company.id, materialReview.id); setProfileImprovedMaterialIds((current) => new Set(current).add(materialReview.id)); setMaterialReview(null); onProfileConfirmed?.(nextProfile); }} /> : null}
    <ExternalResearchAssistModal
      company={company}
      profile={profile}
      targets={externalAssist?.targets}
      objective={externalAssist?.objective}
      open={externalAssist !== null}
      onClose={() => setExternalAssist(null)}
      onSaved={() => investigations?.onRefresh?.()}
    />
    </div>
  );
}
