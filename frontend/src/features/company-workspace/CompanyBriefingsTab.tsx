import { useCallback, useEffect, useState } from "react";
import { getApiErrorMessage } from "../../api/client";
import { createBriefing, getBriefing, getBriefings, updateBriefing, type Briefing, type BriefingListItem, type BriefingTemplate } from "../../api/briefings";
import { getInvestigations, type Investigation } from "../../api/investigations";
import { BriefingEditor } from "./BriefingEditor";
import { BriefingWorkspace } from "./BriefingWorkspace";
import styles from "./company-briefings.module.css";

interface Props {
  companyId: string; initialInvestigationId?: string | null; onSeedConsumed?: () => void;
  onDeepResearch?: (objective: string) => void; onExternalResearch?: (objective: string) => void;
}

export function CompanyBriefingsTab({ companyId, initialInvestigationId, onSeedConsumed, onDeepResearch, onExternalResearch }: Props) {
  const [briefings, setBriefings] = useState<BriefingListItem[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [selected, setSelected] = useState<Briefing | null>(null);
  const [investigations, setInvestigations] = useState<Investigation[]>([]);
  const [editorOpen, setEditorOpen] = useState(false);
  const [initialTemplate, setInitialTemplate] = useState<BriefingTemplate | undefined>();
  const [includedBriefingIds, setIncludedBriefingIds] = useState<string[]>([]);
  const [checkingBriefingTargets, setCheckingBriefingTargets] = useState(false);
  const [busy, setBusy] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    const [list, material] = await Promise.all([getBriefings(companyId), getInvestigations(companyId)]);
    setBriefings(list); setInvestigations(material);
    setSelectedId(current => current && list.some(item => item.id === current) ? current : list[0]?.id ?? null);
  }, [companyId]);
  useEffect(() => { let active = true; setLoading(true); void load().catch(reason => { if (active) setError(getApiErrorMessage(reason, "Could not load Briefings.")); }).finally(() => { if (active) setLoading(false); }); return () => { active = false; }; }, [load]);
  useEffect(() => {
    if (!selectedId) { setSelected(null); return; }
    let active = true;
    void getBriefing(companyId, selectedId).then(value => { if (active) setSelected(value); }).catch(reason => { if (active) setError(getApiErrorMessage(reason, "Could not open Briefing.")); });
    return () => { active = false; };
  }, [companyId, selectedId]);
  useEffect(() => { if (initialInvestigationId && briefings.length === 0 && !loading) setEditorOpen(true); }, [briefings.length, initialInvestigationId, loading]);
  useEffect(() => {
    if (!initialInvestigationId || !briefings.length) { setIncludedBriefingIds([]); setCheckingBriefingTargets(false); return; }
    let active = true;
    setCheckingBriefingTargets(true);
    void Promise.all(briefings.map(async item => {
      try { const value = await getBriefing(companyId, item.id); return value.currentVersion.sources.some(source => source.investigationId === initialInvestigationId) ? item.id : null; }
      catch { return null; }
    })).then(ids => { if (active) { setIncludedBriefingIds(ids.filter((id): id is string => id !== null)); setCheckingBriefingTargets(false); } });
    return () => { active = false; };
  }, [briefings, companyId, initialInvestigationId]);

  const changed = (brief: Briefing) => { setSelected(brief); setSelectedId(brief.id); void load().catch(() => undefined); };
  const create = async (value: { title: string; template: Briefing["template"]; objective: string; investigationIds: string[] }) => {
    setBusy(true); setError(null);
    try { const brief = await createBriefing(companyId, value); changed(brief); setEditorOpen(false); onSeedConsumed?.(); }
    catch (reason) { setError(getApiErrorMessage(reason, "Briefing generation failed. No Briefing was saved.")); }
    finally { setBusy(false); }
  };
  const addToExisting = async (briefingId: string) => {
    if (!initialInvestigationId || includedBriefingIds.includes(briefingId)) return;
    setBusy(true); setError(null);
    try { const updated = await updateBriefing(companyId, briefingId, { newInvestigationIds: [initialInvestigationId] }); changed(updated); onSeedConsumed?.(); }
    catch (reason) { setError(getApiErrorMessage(reason, "Could not add this Investigation to the Briefing.")); }
    finally { setBusy(false); }
  };

  return <section className={styles.page} aria-labelledby="company-briefings-heading">
    <header className={styles.header}><div><p className={styles.eyebrow}>COMPANY · RESEARCH BRIEFINGS</p><h2 id="company-briefings-heading">Briefings <span className={styles.briefingCount}>{briefings.length || ""}</span></h2><p>Curated thematic intelligence built from selected Investigations.</p></div><button className="button" type="button" onClick={() => { setInitialTemplate(undefined); setEditorOpen(true); }}>Create briefing</button></header>
    {error ? <p role="alert">{error}</p> : null}
    {loading ? <p role="status">Loading Briefings…</p> : null}
    {initialInvestigationId && briefings.length ? <div className={styles.notice}><h3>Add Investigation to Briefing</h3><p>Adding new material creates a new immutable version. Briefings that already include this Investigation are unavailable.</p><div className={styles.actions}>{briefings.map(item => {
      const included = includedBriefingIds.includes(item.id);
      return <button className="button button--secondary" key={item.id} type="button" disabled={busy || checkingBriefingTargets || included} onClick={() => void addToExisting(item.id)}><strong>{item.title}</strong><small>{checkingBriefingTargets ? "Checking selected material…" : included ? "Already included" : `v${item.versionNumber} · Research through ${new Date(item.researchThrough).toLocaleDateString()}`}</small></button>;
    })}<button className="button button--quiet" type="button" onClick={() => { setInitialTemplate(undefined); setEditorOpen(true); }}>Create new briefing</button><button className="button button--quiet" type="button" onClick={onSeedConsumed}>Cancel</button></div></div> : null}
    {!loading && briefings.length === 0 ? <>
      <section className={styles.emptyHero} aria-label="Create a research Briefing"><div><p className={styles.heroLabel}>Built from existing research</p><h3>Turn selected research into reusable company intelligence.</h3><p>Briefings combine the Investigations you choose into a durable thematic view with retained sources and version history. Creating one does not require a usable Profile.</p><button className="button" type="button" onClick={() => { setInitialTemplate(undefined); setEditorOpen(true); }}>Create your first Briefing</button></div><div className={styles.availableResearch}><strong>{investigations.filter(item => item.status === "Ready" || item.status === "Done").length}</strong><span>ready or done Investigations</span><strong>{new Set(investigations.flatMap(item => item.topics)).size}</strong><span>topics represented</span></div></section>
      <section className={styles.templateSection}><h3>Popular starting points</h3><div className={styles.templateGrid}>{(["Talent & Hiring", "Markets & Expansion", "Business Model"] as BriefingTemplate[]).map((template, index) => <article key={template}><p>{["Hiring signals, roles and geographic activity", "Markets, locations and expansion activity", "Customers, channels, partners and revenue model"][index]}</p><h4>{template}</h4><button className="button button--secondary" type="button" onClick={() => { setInitialTemplate(template); setEditorOpen(true); }}>Start with this template</button></article>)}</div></section>
    </> : null}
    {briefings.length ? <div className={styles.grid}><nav className={styles.list} aria-label="Choose a Briefing">{briefings.map(item => <button type="button" key={item.id} aria-current={item.id === selectedId ? "true" : undefined} onClick={() => setSelectedId(item.id)}><strong>{item.title}</strong><small>v{item.versionNumber} · Research through {new Date(item.researchThrough).toLocaleDateString()}</small>{item.newerRelevantCount ? <small className={styles.newResearchMarker}>● {item.newerRelevantCount} newer Investigations</small> : null}</button>)}</nav>{selected ? <BriefingWorkspace companyId={companyId} briefing={selected} onChanged={changed} onDeepResearch={onDeepResearch} onExternalResearch={onExternalResearch} /> : null}</div> : null}
    <BriefingEditor open={editorOpen} investigations={investigations} initialInvestigationId={initialInvestigationId} initialTemplate={initialTemplate} busy={busy} error={error} onClose={() => { setEditorOpen(false); onSeedConsumed?.(); }} onCreate={value => void create(value)} />
  </section>;
}
