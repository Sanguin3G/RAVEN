import { useCallback, useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { getApiErrorMessage } from "../../api/client";
import { getInvestigations, getInvestigationOrganization, markInvestigationDone, organizeInvestigation, reopenInvestigation, type Investigation, type InvestigationOrganization } from "../../api/investigations";
import type { ResearchTarget } from "../../api/coverage";
import { getBriefings, type BriefingListItem } from "../../api/briefings";
import { dismissResearchActivity } from "../../utils/researchActivity";
import type { DossierProfile } from "./dossierTypes";
import { InvestigationWorkspace } from "./InvestigationWorkspace";
import { InvestigationSwitcher } from "./InvestigationSwitcher";
import styles from "./company-investigations.module.css";

const topicTargets: Record<string, ResearchTarget> = {
  "Legal Identity": "LegalIdentity", "Tax / Registration": "TaxRegistration", "Founded / History": "FoundedHistory",
  Industry: "Industry", "Employee Scale": "EmployeeScale", "Products & Services": "ProductsServices",
  Markets: "Markets", Leadership: "Leadership", Locations: "Locations",
};

interface Props {
  companyId: string; companyName?: string; profile?: DossierProfile | null;
  onOpenExternalResearch?: (objective: string) => void;
  onOpenDeepResearch?: (objective?: string) => void;
  onImproveProfile?: (targets: ResearchTarget[], sourceMaterialId?: string, sourceMaterialKind?: "saved" | "managed") => void;
  onAddToBriefing?: (investigationId: string) => void;
}

export function CompanyInvestigationsTab({ companyId, companyName = "Company", profile, onOpenExternalResearch, onOpenDeepResearch, onImproveProfile, onAddToBriefing }: Props) {
  const [searchParams] = useSearchParams();
  const requestedId = searchParams.get("research");
  const [items, setItems] = useState<Investigation[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [organizations, setOrganizations] = useState<Record<string, InvestigationOrganization | null>>({});
  const [briefings, setBriefings] = useState<BriefingListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    const loaded = await getInvestigations(companyId);
    setItems(loaded);
    setSelectedId(current => {
      const requested = loaded.find(item => item.id === requestedId || item.materialId === requestedId);
      if (requested) return requested.id;
      return current && loaded.some(item => item.id === current) ? current : loaded[0]?.id ?? null;
    });
  }, [companyId, requestedId]);

  useEffect(() => {
    let active = true;
    setLoading(true);
    void refresh().catch(reason => { if (active) setError(getApiErrorMessage(reason, "Could not load investigations.")); }).finally(() => { if (active) setLoading(false); });
    const interval = window.setInterval(() => { void refresh().catch(() => undefined); }, 8_000);
    return () => { active = false; window.clearInterval(interval); };
  }, [refresh]);
  useEffect(() => { void getBriefings(companyId).then(setBriefings).catch(() => undefined); }, [companyId]);

  const selected = useMemo(() => items.find(item => item.id === selectedId) ?? null, [items, selectedId]);
  useEffect(() => {
    if (!selected || selected.materialKind !== "Saved" || Object.hasOwn(organizations, selected.id)) return;
    let active = true;
    void getInvestigationOrganization(companyId, selected.id).then(value => { if (active) setOrganizations(current => ({ ...current, [selected.id]: value })); })
      .catch(() => { if (active) setOrganizations(current => ({ ...current, [selected.id]: null })); });
    return () => { active = false; };
  }, [companyId, organizations, selected]);
  useEffect(() => { if (selected?.materialKind === "Managed" && selected.status === "Ready") dismissResearchActivity(`deep-${selected.id}`); }, [selected]);

  const run = async (action: () => Promise<unknown>) => {
    setBusy(true); setError(null);
    try { await action(); await refresh(); }
    catch (reason) { setError(getApiErrorMessage(reason, "Could not update this Investigation.")); }
    finally { setBusy(false); }
  };
  const analyze = async () => {
    if (!selected || selected.materialKind !== "Saved") return;
    await run(async () => {
      const value = await organizeInvestigation(companyId, selected.id);
      setOrganizations(current => ({ ...current, [selected.id]: value }));
    });
  };
  const targets = selected ? [...new Set(selected.topics.map(topic => topicTargets[topic]).filter((target): target is ResearchTarget => Boolean(target)))] : [];
  return <section className={styles.investigationsPage} aria-labelledby="dossier-investigations-heading" data-testid="dossier-investigations">
    <header className={styles.investigationsPageHeader}><div><p className={styles.eyebrow}>COMPANY · INVESTIGATIONS</p><h2 id="dossier-investigations-heading">Investigations</h2><p>Research questions, findings, source leads, and uncertainty.</p></div></header>
    {loading ? <p role="status">Loading investigations…</p> : null}
    {error ? <p className={styles.errorMessage} role="alert">{error}</p> : null}
    {!loading && items.length === 0 ? <div className={styles.emptyState}><h3>No investigations yet</h3><p>Research a focused question to build a reusable record.</p><button className="button" type="button" onClick={() => onOpenDeepResearch?.()}>Open Deep Research in Ask RAVEN</button></div> : null}
    {items.length > 0 ? <>
      <InvestigationSwitcher items={items} selectedId={selectedId} onSelect={setSelectedId} />
      {selected ? <InvestigationWorkspace
        companyName={companyName} profile={profile}
        investigation={{ ...selected, organization: organizations[selected.id] }}
        usedInBriefings={briefings.filter(brief => selected.briefingIds.includes(brief.id)).map(brief => brief.title)}
        busy={busy}
        onResearchFurther={() => onOpenDeepResearch?.(selected.objective)}
        onOpenExternalResearch={() => onOpenExternalResearch?.(selected.objective)}
        onMarkDone={selected.status === "Ready" ? () => void run(() => markInvestigationDone(companyId, selected.id)) : undefined}
        onReopen={selected.status === "Done" ? () => void run(() => reopenInvestigation(companyId, selected.id)) : undefined}
        onAnalyze={selected.materialKind === "Saved" && selected.status !== "Running" && selected.status !== "Failed" ? () => void analyze() : undefined}
        onAddToBriefing={onAddToBriefing && (selected.status === "Ready" || selected.status === "Done") ? () => onAddToBriefing(selected.id) : undefined}
        onImproveProfile={onImproveProfile && selected.purpose === "ProfileImprovement" && selected.status === "Ready" && !selected.profileImprovementLocked && !selected.appliedProfileVersionId && targets.length > 0 && selected.materialId
          ? () => onImproveProfile?.(targets, selected.materialId!, selected.materialKind === "Saved" ? "saved" : "managed") : undefined}
      /> : null}
    </> : null}
  </section>;
}
