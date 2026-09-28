import { useCallback, useEffect, useState } from "react";
import { getApiErrorMessage } from "../../api/client";
import { createBriefing, getBriefing, getBriefings, updateBriefing, type Briefing, type BriefingGenerationJob, type BriefingListItem, type BriefingTemplate } from "../../api/briefings";
import { getInvestigations, type Investigation } from "../../api/investigations";
import { upsertResearchActivity } from "../../utils/researchActivity";
import { BriefingEditor } from "./BriefingEditor";
import { BriefingGenerationStatus } from "./BriefingGenerationStatus";
import { BriefingWorkspace } from "./BriefingWorkspace";
import styles from "./company-briefings.module.css";

type CreateBody = { title: string; template: BriefingTemplate; objective: string; investigationIds: string[] };
type UpdateBody = { newInvestigationIds: string[]; title?: string; template?: BriefingTemplate; objective?: string };

interface Props {
  companyId: string; companyName?: string; initialInvestigationId?: string | null; onSeedConsumed?: () => void;
  initialBriefingId?: string | null; initialBriefingVersionNumber?: number | null;
  onDeepResearch?: (objective: string) => void; onExternalResearch?: (objective: string) => void;
}

export function CompanyBriefingsTab({ companyId, companyName = "Company", initialInvestigationId, initialBriefingId, initialBriefingVersionNumber, onSeedConsumed, onDeepResearch, onExternalResearch }: Props) {
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
  const [generation, setGeneration] = useState<{ job: BriefingGenerationJob; title: string; briefingId: string | null; operation: "create" | "update"; body: CreateBody | UpdateBody } | null>(null);

  const load = useCallback(async () => {
    const [list, material] = await Promise.all([getBriefings(companyId), getInvestigations(companyId)]);
    setBriefings(list); setInvestigations(material);
    setSelectedId(current => initialBriefingId && list.some(item => item.id === initialBriefingId)
      ? initialBriefingId : current && list.some(item => item.id === current) ? current : list[0]?.id ?? null);
  }, [companyId, initialBriefingId]);
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

  const trackGeneration = (job: BriefingGenerationJob, title: string, briefingId: string | null, operation: "create" | "update", body: CreateBody | UpdateBody) => {
    setGeneration({ job, title, briefingId, operation, body });
    upsertResearchActivity({ id: `briefing-${job.id}`, jobId: job.id, origin: "Briefing", companyId, companyName, objective: title,
      detail: "Generating briefing…", status: "running", href: `/companies/${encodeURIComponent(companyId)}?tab=briefings`, updatedAt: job.updatedAt });
  };
  const changed = useCallback((brief: Briefing) => { setSelected(brief); setSelectedId(brief.id); void load().catch(() => undefined); }, [load]);
  const runUpdate = async (briefingId: string, body: UpdateBody, title: string) => {
    setBusy(true); setError(null);
    try { const job = await updateBriefing(companyId, briefingId, body); trackGeneration(job, title, briefingId, "update", body); }
    catch (reason) { setError(getApiErrorMessage(reason, "Could not start Briefing generation.")); throw reason; }
    finally { setBusy(false); }
  };
  const create = async (value: CreateBody) => {
    setBusy(true); setError(null);
    try { const job = await createBriefing(companyId, value); trackGeneration(job, value.title, null, "create", value); setEditorOpen(false); onSeedConsumed?.(); }
    catch (reason) { setError(getApiErrorMessage(reason, "Briefing generation failed. No Briefing was saved.")); }
    finally { setBusy(false); }
  };
  const addToExisting = async (briefingId: string) => {
    if (!initialInvestigationId || includedBriefingIds.includes(briefingId)) return;
    setBusy(true); setError(null);
    try { const title = briefings.find(item => item.id === briefingId)?.title ?? "Briefing"; await runUpdate(briefingId, { newInvestigationIds: [initialInvestigationId] }, title); onSeedConsumed?.(); }
    catch (reason) { setError(getApiErrorMessage(reason, "Could not add this Investigation to the Briefing.")); }
    finally { setBusy(false); }
  };

  const completeGeneration = useCallback(async (briefingId: string) => {
    try { changed(await getBriefing(companyId, briefingId)); }
    catch (reason) { setError(getApiErrorMessage(reason, "Briefing was generated but could not be opened.")); }
    finally { setGeneration(null); }
  }, [companyId, changed]);

  const retryGeneration = async () => {
    if (!generation) return;
    const previous = generation;
    try {
      const job = previous.operation === "create"
        ? await createBriefing(companyId, previous.body as CreateBody)
        : await updateBriefing(companyId, previous.briefingId!, previous.body as UpdateBody);
      trackGeneration(job, previous.title, previous.briefingId, previous.operation, previous.body);
    } catch (reason) { setError(getApiErrorMessage(reason, "Could not retry Briefing generation.")); }
  };

  return <section className={styles.page} aria-labelledby="company-briefings-heading">
    <header className={styles.header}><div><p className={styles.eyebrow}>COMPANY · RESEARCH BRIEFINGS</p><h2 id="company-briefings-heading">Briefings <span className={styles.briefingCount}>{briefings.length || ""}</span></h2><p>Curated thematic intelligence built from selected Investigations.</p></div><button className="button" type="button" onClick={() => { setInitialTemplate(undefined); setEditorOpen(true); }}>Create briefing</button></header>
    {error ? <p role="alert">{error}</p> : null}
    {loading ? <p role="status">Loading Briefings…</p> : null}
    {initialInvestigationId && briefings.length ? <div className={styles.notice}><h3>Add Investigation to Briefing</h3><p>Adding new material creates a new immutable version. Briefings that already include this Investigation are unavailable.</p><div className={styles.actions}>{briefings.map(item => {
      const included = includedBriefingIds.includes(item.id);
      return <div className={styles.targetRow} key={item.id}><span><strong>{item.title}</strong><small>{checkingBriefingTargets ? "Checking selected material…" : `v${item.versionNumber} · Research through ${new Date(item.researchThrough).toLocaleDateString()}`}</small></span>{included ? <small className={styles.includedState}>✓ Included</small> : <button className="button button--secondary" type="button" disabled={busy || checkingBriefingTargets} onClick={() => void addToExisting(item.id)}>Add</button>}</div>;
    })}<button className="button button--quiet" type="button" onClick={() => { setInitialTemplate(undefined); setEditorOpen(true); }}>Create new briefing</button><button className="button button--quiet" type="button" onClick={onSeedConsumed}>Cancel</button></div></div> : null}
    {!loading && briefings.length === 0 ? <>
      <section className={styles.emptyHero} aria-label="Create a research Briefing"><div><p className={styles.heroLabel}>Built from existing research</p><h3>Turn selected research into reusable company intelligence.</h3><p>Briefings combine the Investigations you choose into a durable thematic view with retained sources and version history. Creating one does not require a usable Profile.</p><button className="button" type="button" onClick={() => { setInitialTemplate(undefined); setEditorOpen(true); }}>Create your first Briefing</button></div><div className={styles.availableResearch}><strong>{investigations.filter(item => item.status === "Ready" || item.status === "Done").length}</strong><span>ready or done Investigations</span><strong>{new Set(investigations.flatMap(item => item.topics)).size}</strong><span>topics represented</span></div></section>
      <section className={styles.templateSection}><h3>Popular starting points</h3><div className={styles.templateGrid}>{(["Talent & Hiring", "Markets & Expansion", "Business Model"] as BriefingTemplate[]).map((template, index) => <article key={template}><p>{["Hiring signals, roles and geographic activity", "Markets, locations and expansion activity", "Customers, channels, partners and revenue model"][index]}</p><h4>{template}</h4><button className="button button--secondary" type="button" onClick={() => { setInitialTemplate(template); setEditorOpen(true); }}>Start with this template</button></article>)}</div></section>
    </> : null}
    {briefings.length ? <div className={styles.grid}><nav className={styles.list} aria-label="Choose a Briefing">{briefings.map(item => <button type="button" key={item.id} aria-current={item.id === selectedId ? "true" : undefined} onClick={() => setSelectedId(item.id)}><strong>{item.title}</strong><small>v{item.versionNumber} · Research through {new Date(item.researchThrough).toLocaleDateString()}</small>{item.newerRelevantCount ? <small className={styles.newResearchMarker}>● {item.newerRelevantCount} newer Investigations</small> : null}</button>)}</nav>{selected ? <BriefingWorkspace companyId={companyId} briefing={selected} initialVersionNumber={selected.id === initialBriefingId ? initialBriefingVersionNumber : null} onGenerate={runUpdate} onDeepResearch={onDeepResearch} onExternalResearch={onExternalResearch} /> : null}</div> : null}
    <BriefingEditor open={editorOpen} investigations={investigations} initialInvestigationId={initialInvestigationId} initialTemplate={initialTemplate} busy={busy} error={error} onClose={() => { setEditorOpen(false); onSeedConsumed?.(); }} onCreate={value => void create(value)} />
    {generation ? <BriefingGenerationStatus companyId={companyId} title={generation.title} initialJob={generation.job} onMinimize={() => setGeneration(null)} onRetry={() => void retryGeneration()} onCompleted={completeGeneration} /> : null}
  </section>;
}
