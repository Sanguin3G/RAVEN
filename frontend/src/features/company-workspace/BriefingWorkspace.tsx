import { useEffect, useRef, useState } from "react";
import { getApiErrorMessage } from "../../api/client";
import { briefingTemplates, getBriefingChanges, getBriefingVersion, getBriefingVersions, getNewerBriefingInvestigations, updateBriefing,
  type Briefing, type BriefingCandidate, type BriefingChange, type BriefingTemplate, type BriefingVersion } from "../../api/briefings";
import { BriefingResearchDialog } from "./BriefingResearchDialog";
import styles from "./company-briefings.module.css";

const date = (value: string) => new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short", year: "numeric" }).format(new Date(value));

interface Props {
  companyId: string; briefing: Briefing; onChanged: (value: Briefing) => void;
  initialVersionNumber?: number | null;
  onDeepResearch?: (objective: string) => void; onExternalResearch?: (objective: string) => void;
}

export function BriefingWorkspace({ companyId, briefing, initialVersionNumber, onChanged, onDeepResearch, onExternalResearch }: Props) {
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
  const historyDialog = useRef<HTMLDialogElement>(null);
  const changesDialog = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    let active = true;
    setViewed(briefing.currentVersion); setVersions(null); setChanges(null); setTitle(briefing.title); setTemplate(briefing.template); setObjective(briefing.objective);
    if (initialVersionNumber && initialVersionNumber !== briefing.currentVersion.versionNumber) {
      void getBriefingVersion(companyId, briefing.id, initialVersionNumber)
        .then((version) => { if (active) setViewed(version); })
        .catch((reason) => { if (active) setError(getApiErrorMessage(reason, "Could not open the cited Briefing version.")); });
    }
    return () => { active = false; };
  }, [briefing.id, briefing.currentVersion, companyId, initialVersionNumber]);
  useEffect(() => { if (updateOpen && !updateDialog.current?.open) updateDialog.current?.showModal(); if (!updateOpen && updateDialog.current?.open) updateDialog.current.close(); }, [updateOpen]);
  useEffect(() => { if (editOpen && !editDialog.current?.open) editDialog.current?.showModal(); if (!editOpen && editDialog.current?.open) editDialog.current.close(); }, [editOpen]);
  useEffect(() => { if (versions && !historyDialog.current?.open) historyDialog.current?.showModal(); if (!versions && historyDialog.current?.open) historyDialog.current.close(); }, [versions]);
  useEffect(() => { if (changes && !changesDialog.current?.open) changesDialog.current?.showModal(); if (!changes && changesDialog.current?.open) changesDialog.current.close(); }, [changes]);
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
    setViewed(version); setChanges(null); setVersions(null);
    if (version.versionNumber > 1) {
      try { setChanges(await getBriefingChanges(companyId, briefing.id, version.versionNumber)); }
      catch { setChanges(null); }
    }
  };
  const returnToCurrent = () => { setViewed(briefing.currentVersion); setChanges(null); };
  const isHistorical = viewed.id !== briefing.currentVersion.id;

  return <article className={styles.workspace} aria-labelledby="briefing-title">
    <header><p>Research briefing · {viewed.versionNumber === briefing.currentVersion.versionNumber ? `v${viewed.versionNumber} · Current` : `v${viewed.versionNumber} · Historical version`}</p><h2 id="briefing-title">{viewed.title}</h2>
      <p className={styles.meta}>Research through {date(viewed.researchThrough)} · Generated {date(viewed.generatedAt)} · Based on {viewed.sources.length} Investigation{viewed.sources.length === 1 ? "" : "s"}</p>
      <p className={styles.meta}>Built from selected research material. This is not accepted Company Profile truth.</p>
    </header>
    {isHistorical ? <div className={styles.historicalBanner}><p><strong>Viewing historical version v{viewed.versionNumber}.</strong> The current Briefing is v{briefing.currentVersion.versionNumber}.</p><button className="button button--secondary" type="button" onClick={returnToCurrent}>Return to current</button></div> : null}
    <div className={styles.actions}>
      <button className="button" type="button" onClick={() => setUpdateOpen(true)}>Update briefing</button>
      <details><summary className="button button--secondary">Research ▾</summary><button className="button button--quiet" type="button" onClick={() => setResearchMode("latest")}>Research latest</button><button className="button button--quiet" type="button" onClick={() => setResearchMode("gaps")}>Research gaps</button></details>
      <details><summary className="button button--quiet">More ▾</summary><button className="button button--quiet" type="button" onClick={() => setEditOpen(true)}>Edit briefing</button><button className="button button--quiet" type="button" disabled={busy} onClick={() => void generate({ newInvestigationIds: [] })}>Regenerate</button><button className="button button--quiet" type="button" onClick={() => void showVersions()}>Version history</button></details>
    </div>
    {error ? <p role="alert">{error}</p> : null}
    {briefing.newerRelevantCount > 0 && !isHistorical ? <section className={styles.newResearchNotice} aria-label="New research available"><div><h3>● New research available</h3><p>{briefing.newerRelevantCount} newer Investigation{briefing.newerRelevantCount === 1 ? " may be" : "s may be"} relevant to this Briefing. Research through {date(briefing.currentVersion.researchThrough)}.</p></div><button className="button" type="button" onClick={() => setUpdateOpen(true)}>Review new research</button></section> : null}
    <div className={styles.sections}>{viewed.sections.map(section => {
      const lower = section.title.toLowerCase();
      const uncertainty = lower.includes("uncertaint") || lower.includes("open question");
      return <section className={`${styles.section} ${uncertainty ? styles.uncertaintySection : ""}`} key={section.key}>
        <h3>{section.title}</h3>{section.items.length ? <ul>{section.items.map((item, itemIndex) => <li key={`${item}-${itemIndex}`}>{item}</li>)}</ul> : <p>No supported finding in the selected material.</p>}
        {section.sourceInvestigationIds.length ? <small>Based on {section.sourceInvestigationIds.map(id => viewed.sources.find(source => source.investigationId === id)?.title || "Investigation").join(", ")}</small> : null}
      </section>;
    })}</div>
    <details className={styles.sourceDetails}><summary>Source Investigations ({viewed.sources.length})</summary><ul className={styles.sourceList}>{viewed.sources.map(source => <li key={source.investigationId}><strong>{source.title}</strong><small>{source.origin} · {source.purpose === "ProfileImprovement" ? "Profile improvement" : "General research"} · Researched {date(source.materialUpdatedAt)}</small></li>)}</ul></details>

    <dialog ref={historyDialog} className={styles.dialog} aria-labelledby="briefing-history-heading" onClose={() => setVersions(null)}>
      <header><h2 id="briefing-history-heading">Version history</h2><button type="button" aria-label="Close version history" onClick={() => setVersions(null)}>×</button></header>
      <p>Each version is an immutable snapshot of the Briefing and its selected research.</p>
      <div className={styles.versionList}>{versions?.map(version => <article key={version.id}><div><strong>v{version.versionNumber}{version.id === briefing.currentVersion.id ? " · Current" : ""}</strong><small>{date(version.generatedAt)} · Research through {date(version.researchThrough)}</small></div><button className="button button--secondary" type="button" aria-current={viewed.id === version.id ? "true" : undefined} onClick={() => void view(version)}>View{version.versionNumber > 1 ? ` / Compare with v${version.versionNumber - 1}` : ""}</button></article>)}</div>
    </dialog>
    <dialog ref={changesDialog} className={styles.dialog} aria-labelledby="briefing-changes-heading" onClose={() => setChanges(null)}>
      <header><h2 id="briefing-changes-heading">What changed · v{changes?.fromVersion} → v{changes?.toVersion}</h2><button type="button" aria-label="Close changes" onClick={() => setChanges(null)}>×</button></header>
      <p>Comparison of Briefing versions; wording changes do not establish changes in the company.</p>
      {changes && ([ ["New material", changes.newMaterial], ["Changed sections", changes.changedMaterial], ["No longer present in selected material", changes.removedMaterial], ["New uncertainty", changes.newUncertainties] ] as const).map(([label, values]) => <section key={label}><h3>{label}</h3>{values.length ? <ul>{values.map((value, index) => <li key={`${value}-${index}`}>{value}</li>)}</ul> : <p>None recorded.</p>}</section>)}
      <footer><button className="button button--secondary" type="button" onClick={() => setChanges(null)}>Close</button></footer>
    </dialog>
    <dialog ref={updateDialog} className={styles.dialog} aria-labelledby="briefing-update-heading" onClose={() => setUpdateOpen(false)}><form onSubmit={event => { event.preventDefault(); void generate({ newInvestigationIds: selectedNew }); }}>
      <header><h2 id="briefing-update-heading">Update briefing</h2><button type="button" aria-label="Close update" onClick={() => setUpdateOpen(false)}>×</button></header>
      <p>Existing selected research stays included. Choose newer material for a new version; no web research starts here.</p>
      {newer.length ? <div className={styles.selectionList}>{newer.map(item => <label key={item.id}><input type="checkbox" checked={selectedNew.includes(item.id)} onChange={() => setSelectedNew(current => current.includes(item.id) ? current.filter(id => id !== item.id) : [...current, item.id])} /><span><strong>{item.title}</strong><small>{item.origin} · {item.topics.join(", ")} · {date(item.materialUpdatedAt)}</small></span></label>)}</div> : <p>No newer relevant Investigation is available. Use Regenerate to rerun synthesis over the same material.</p>}
      {error ? <p role="alert">{error}</p> : null}<footer><button className="button button--quiet" type="button" onClick={() => setUpdateOpen(false)}>Cancel</button><button className="button" type="submit" disabled={busy || !selectedNew.length}>{busy ? "Updating…" : "Update briefing"}</button></footer>
    </form></dialog>
    <dialog ref={editDialog} className={styles.dialog} aria-labelledby="briefing-edit-heading" onClose={() => setEditOpen(false)}><form onSubmit={event => { event.preventDefault(); void generate({ newInvestigationIds: [], title, template, objective }); }}>
      <header><h2 id="briefing-edit-heading">Edit briefing</h2><button type="button" aria-label="Close edit" onClick={() => setEditOpen(false)}>×</button></header><p>Saving the definition generates a new version from the same selected research.</p>
      <label>Title<input required maxLength={200} value={title} onChange={event => setTitle(event.target.value)} /></label><label>Template<select value={template} onChange={event => setTemplate(event.target.value as BriefingTemplate)}>{briefingTemplates.map(value => <option key={value}>{value}</option>)}</select></label><label>Objective<textarea required maxLength={2000} value={objective} onChange={event => setObjective(event.target.value)} /></label>
      {error ? <p role="alert">{error}</p> : null}<footer><button className="button button--quiet" type="button" onClick={() => setEditOpen(false)}>Cancel</button><button className="button" disabled={busy} type="submit">Save new version</button></footer>
    </form></dialog>
    <BriefingResearchDialog briefing={briefing} mode={researchMode} onClose={() => setResearchMode(null)} onDeepResearch={onDeepResearch} onExternalResearch={onExternalResearch} />
  </article>;
}
