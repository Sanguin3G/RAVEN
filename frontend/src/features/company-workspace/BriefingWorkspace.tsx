import { useEffect, useRef, useState } from "react";
import { getApiErrorMessage } from "../../api/client";
import { briefingTemplates, getBriefingChanges, getBriefingVersions, getNewerBriefingInvestigations, updateBriefing,
  type Briefing, type BriefingCandidate, type BriefingChange, type BriefingTemplate, type BriefingVersion } from "../../api/briefings";
import { BriefingResearchDialog } from "./BriefingResearchDialog";
import styles from "./company-briefings.module.css";

const date = (value: string) => new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short", year: "numeric" }).format(new Date(value));

interface Props {
  companyId: string; briefing: Briefing; onChanged: (value: Briefing) => void;
  onDeepResearch?: (objective: string) => void; onExternalResearch?: (objective: string) => void;
}

export function BriefingWorkspace({ companyId, briefing, onChanged, onDeepResearch, onExternalResearch }: Props) {
  const [viewed, setViewed] = useState<BriefingVersion>(briefing.currentVersion);
  const [versions, setVersions] = useState<BriefingVersion[] | null>(null);
  const [changes, setChanges] = useState<BriefingChange | null>(null);
  const [newer, setNewer] = useState<BriefingCandidate[]>([]);
  const [selectedNew, setSelectedNew] = useState<string[]>([]);
  const [updateOpen, setUpdateOpen] = useState(false);
  const [editOpen, setEditOpen] = useState(false);
  const [researchMode, setResearchMode] = useState<"latest" | "gaps" | null>(null);
  const [title, setTitle] = useState(briefing.title);
  const [template, setTemplate] = useState<BriefingTemplate>(briefing.template);
  const [objective, setObjective] = useState(briefing.objective);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const updateDialog = useRef<HTMLDialogElement>(null);
  const editDialog = useRef<HTMLDialogElement>(null);
  useEffect(() => { setViewed(briefing.currentVersion); setVersions(null); setChanges(null); setTitle(briefing.title); setTemplate(briefing.template); setObjective(briefing.objective); }, [briefing.id, briefing.currentVersion]);
  useEffect(() => { if (updateOpen && !updateDialog.current?.open) updateDialog.current?.showModal(); if (!updateOpen && updateDialog.current?.open) updateDialog.current.close(); }, [updateOpen]);
  useEffect(() => { if (editOpen && !editDialog.current?.open) editDialog.current?.showModal(); if (!editOpen && editDialog.current?.open) editDialog.current.close(); }, [editOpen]);
  useEffect(() => {
    if (!updateOpen) return;
    void getNewerBriefingInvestigations(companyId, briefing.id).then(setNewer).catch(reason => setError(getApiErrorMessage(reason, "Could not load newer Investigations.")));
  }, [briefing.id, companyId, updateOpen]);

  const generate = async (body: { newInvestigationIds: string[]; title?: string; template?: BriefingTemplate; objective?: string }) => {
    setBusy(true); setError(null);
    try { const updated = await updateBriefing(companyId, briefing.id, body); onChanged(updated); setUpdateOpen(false); setEditOpen(false); setSelectedNew([]); }
    catch (reason) { setError(getApiErrorMessage(reason, "Briefing generation failed. Existing versions are unchanged.")); }
    finally { setBusy(false); }
  };
  const showVersions = async () => {
    try { setVersions(await getBriefingVersions(companyId, briefing.id)); }
    catch (reason) { setError(getApiErrorMessage(reason, "Could not load version history.")); }
  };
  const view = async (version: BriefingVersion) => {
    setViewed(version); setChanges(null);
    if (version.versionNumber > 1) {
      try { setChanges(await getBriefingChanges(companyId, briefing.id, version.versionNumber)); }
      catch { setChanges(null); }
    }
  };
  return <article className={styles.workspace} aria-labelledby="briefing-title">
    <header><p>RESEARCH BRIEFING · v{viewed.versionNumber}{viewed.versionNumber === briefing.currentVersion.versionNumber ? " · Current" : ""}</p><h2 id="briefing-title">{viewed.title}</h2><p className={styles.meta}>Generated {date(viewed.generatedAt)} · Research through {date(viewed.researchThrough)} · Based on {viewed.sources.length} Investigation{viewed.sources.length === 1 ? "" : "s"}</p><p className={styles.meta}>Built from selected research material. This is not accepted Company Profile truth.</p></header>
    <div className={styles.actions}>
      <button className="button" type="button" onClick={() => setUpdateOpen(true)}>Update briefing{briefing.newerRelevantCount ? ` · ${briefing.newerRelevantCount} new` : ""}</button>
      <details><summary className="button button--secondary">Research</summary><button className="button button--quiet" type="button" onClick={() => setResearchMode("latest")}>Research latest</button><button className="button button--quiet" type="button" onClick={() => setResearchMode("gaps")}>Research gaps</button></details>
      <details><summary className="button button--quiet">More</summary><button className="button button--quiet" type="button" onClick={() => setEditOpen(true)}>Edit briefing</button><button className="button button--quiet" type="button" disabled={busy} onClick={() => void generate({ newInvestigationIds: [] })}>Regenerate</button><button className="button button--quiet" type="button" onClick={() => void showVersions()}>Version history</button></details>
    </div>
    {error ? <p role="alert">{error}</p> : null}
    {briefing.newerRelevantCount ? <p className={styles.notice}>New research available: {briefing.newerRelevantCount} potentially relevant Investigation{briefing.newerRelevantCount === 1 ? "" : "s"}.</p> : null}
    {versions ? <section aria-label="Version history"><h3>Version history</h3><div className={styles.actions}>{versions.map(version => <button className="button button--quiet" key={version.id} type="button" aria-current={viewed.id === version.id ? "true" : undefined} onClick={() => void view(version)}>v{version.versionNumber} · {date(version.generatedAt)}</button>)}</div></section> : null}
    {changes ? <section className={styles.notice} aria-label="What's changed"><h3>What's changed since v{changes.fromVersion}?</h3><p>Comparison of Briefing versions; changes in wording do not establish changes in the company.</p>{([['New', changes.newMaterial], ['Changed sections', changes.changedMaterial], ['No longer present in selected material', changes.removedMaterial], ['New uncertainty', changes.newUncertainties]] as const).map(([label, values]) => values.length ? <div key={label}><h4>{label}</h4><ul>{values.map((value, index) => <li key={`${value}-${index}`}>{value}</li>)}</ul></div> : null)}</section> : null}
    <div className={styles.sections}>{viewed.sections.map(section => <section className={styles.section} key={section.key}><h3>{section.title}</h3>{section.items.length ? <ul>{section.items.map((item, index) => <li key={`${item}-${index}`}>{item}</li>)}</ul> : <p>No supported finding in the selected material.</p>}{section.sourceInvestigationIds.length ? <small>Based on {section.sourceInvestigationIds.map(id => viewed.sources.find(source => source.investigationId === id)?.title || "Investigation").join(", ")}</small> : null}</section>)}</div>
    <section><h3>Source Investigations</h3><ul className={styles.sourceList}>{viewed.sources.map(source => <li key={source.investigationId}><strong>{source.title}</strong><small> · {source.origin} · {source.purpose === "ProfileImprovement" ? "Profile improvement" : "General research"} · Research {date(source.materialUpdatedAt)}</small></li>)}</ul></section>
    <dialog ref={updateDialog} className={styles.dialog} aria-labelledby="briefing-update-heading" onClose={() => setUpdateOpen(false)}><form onSubmit={event => { event.preventDefault(); void generate({ newInvestigationIds: selectedNew }); }}><header><h2 id="briefing-update-heading">Update briefing</h2><button type="button" aria-label="Close update" onClick={() => setUpdateOpen(false)}>×</button></header><p>Existing selected research stays included. Choose newer material for a new version; no web research starts here.</p>{newer.length ? <div className={styles.selectionList}>{newer.map(item => <label key={item.id}><input type="checkbox" checked={selectedNew.includes(item.id)} onChange={() => setSelectedNew(current => current.includes(item.id) ? current.filter(id => id !== item.id) : [...current, item.id])} /><span><strong>{item.title}</strong><small>{item.origin} · {item.topics.join(", ")} · {date(item.materialUpdatedAt)}</small></span></label>)}</div> : <p>No newer relevant Investigation is available. Use Regenerate to rerun synthesis over the same material.</p>}{error ? <p role="alert">{error}</p> : null}<footer><button className="button button--quiet" type="button" onClick={() => setUpdateOpen(false)}>Cancel</button><button className="button" type="submit" disabled={busy || !selectedNew.length}>{busy ? "Updating…" : "Update briefing"}</button></footer></form></dialog>
    <dialog ref={editDialog} className={styles.dialog} aria-labelledby="briefing-edit-heading" onClose={() => setEditOpen(false)}><form onSubmit={event => { event.preventDefault(); void generate({ newInvestigationIds: [], title, template, objective }); }}><header><h2 id="briefing-edit-heading">Edit briefing</h2><button type="button" aria-label="Close edit" onClick={() => setEditOpen(false)}>×</button></header><p>Saving the definition generates a new version from the same selected research.</p><label>Title<input required maxLength={200} value={title} onChange={event => setTitle(event.target.value)} /></label><label>Template<select value={template} onChange={event => setTemplate(event.target.value as BriefingTemplate)}>{briefingTemplates.map(value => <option key={value}>{value}</option>)}</select></label><label>Objective<textarea required maxLength={2000} value={objective} onChange={event => setObjective(event.target.value)} /></label>{error ? <p role="alert">{error}</p> : null}<footer><button className="button button--quiet" type="button" onClick={() => setEditOpen(false)}>Cancel</button><button className="button" disabled={busy} type="submit">Save new version</button></footer></form></dialog>
    <BriefingResearchDialog briefing={briefing} mode={researchMode} onClose={() => setResearchMode(null)} onDeepResearch={onDeepResearch} onExternalResearch={onExternalResearch} />
  </article>;
}
