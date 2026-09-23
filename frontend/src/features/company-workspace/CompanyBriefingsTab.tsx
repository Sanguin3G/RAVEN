import { useCallback, useEffect, useState } from "react";
import { getApiErrorMessage } from "../../api/client";
import { createBriefing, getBriefing, getBriefings, updateBriefing, type Briefing, type BriefingListItem } from "../../api/briefings";
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

  const changed = (brief: Briefing) => { setSelected(brief); setSelectedId(brief.id); void load().catch(() => undefined); };
  const create = async (value: { title: string; template: Briefing["template"]; objective: string; investigationIds: string[] }) => {
    setBusy(true); setError(null);
    try { const brief = await createBriefing(companyId, value); changed(brief); setEditorOpen(false); onSeedConsumed?.(); }
    catch (reason) { setError(getApiErrorMessage(reason, "Briefing generation failed. No Briefing was saved.")); }
    finally { setBusy(false); }
  };
  const addToExisting = async (briefingId: string) => {
    if (!initialInvestigationId) return;
    setBusy(true); setError(null);
    try { const updated = await updateBriefing(companyId, briefingId, { newInvestigationIds: [initialInvestigationId] }); changed(updated); onSeedConsumed?.(); }
    catch (reason) { setError(getApiErrorMessage(reason, "Could not add this Investigation to the Briefing.")); }
    finally { setBusy(false); }
  };

  return <section className={styles.page} aria-labelledby="company-briefings-heading">
    <header className={styles.header}><div><p>COMPANY · RESEARCH BRIEFINGS</p><h2 id="company-briefings-heading">Briefings</h2><p>Durable thematic views built from research you select.</p></div><button className="button" type="button" onClick={() => setEditorOpen(true)}>Create briefing</button></header>
    {error ? <p role="alert">{error}</p> : null}
    {loading ? <p role="status">Loading Briefings…</p> : null}
    {initialInvestigationId && briefings.length ? <div className={styles.notice}><h3>Add Investigation to briefing</h3><p>Adding it creates a new immutable Briefing version.</p><div className={styles.actions}>{briefings.map(item => <button className="button button--secondary" key={item.id} type="button" disabled={busy} onClick={() => void addToExisting(item.id)}>{item.title}</button>)}<button className="button button--quiet" type="button" onClick={() => setEditorOpen(true)}>Create new briefing</button><button className="button button--quiet" type="button" onClick={onSeedConsumed}>Cancel</button></div></div> : null}
    {!loading && briefings.length === 0 ? <div className={styles.notice}><h3>No Briefings yet</h3><p>Turn selected Investigations into focused, reusable company Briefings. A usable accepted Profile is not required.</p></div> : null}
    {briefings.length ? <div className={styles.grid}><nav className={styles.list} aria-label="Company Briefings">{briefings.map(item => <button type="button" key={item.id} aria-current={item.id === selectedId ? "true" : undefined} onClick={() => setSelectedId(item.id)}><strong>{item.title}</strong><small>v{item.versionNumber} · Research through {new Date(item.researchThrough).toLocaleDateString()}</small>{item.newerRelevantCount ? <small>New research available · {item.newerRelevantCount}</small> : null}</button>)}</nav>{selected ? <BriefingWorkspace companyId={companyId} briefing={selected} onChanged={changed} onDeepResearch={onDeepResearch} onExternalResearch={onExternalResearch} /> : null}</div> : null}
    <BriefingEditor open={editorOpen} investigations={investigations} initialInvestigationId={initialInvestigationId} busy={busy} error={error} onClose={() => { setEditorOpen(false); onSeedConsumed?.(); }} onCreate={value => void create(value)} />
  </section>;
}
