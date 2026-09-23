import { useEffect, useMemo, useRef, useState } from "react";
import type { Investigation } from "../../api/investigations";
import { briefingTemplates, suggestedForTemplate, type BriefingTemplate } from "../../api/briefings";
import styles from "./company-briefings.module.css";

interface Props {
  open: boolean; investigations: Investigation[]; initialInvestigationId?: string | null; initialTemplate?: BriefingTemplate;
  busy: boolean; error?: string | null; onClose: () => void;
  onCreate: (value: { title: string; template: BriefingTemplate; objective: string; investigationIds: string[] }) => void;
}

const readyMaterial = (investigations: Investigation[]) => investigations
  .filter(item => item.status === "Ready" || item.status === "Done")
  .sort((left, right) => Date.parse(right.materialUpdatedAt) - Date.parse(left.materialUpdatedAt));

function researchDate(items: Investigation[]) {
  const newest = items.map(item => Date.parse(item.materialUpdatedAt)).filter(Number.isFinite).sort((a, b) => b - a)[0];
  return newest ? new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric" }).format(new Date(newest)) : null;
}

export function BriefingEditor({ open, investigations, initialInvestigationId, initialTemplate, busy, error, onClose, onCreate }: Props) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [template, setTemplate] = useState<BriefingTemplate>("Talent & Hiring");
  const [title, setTitle] = useState("Talent & Hiring");
  const [objective, setObjective] = useState("");
  const [selected, setSelected] = useState<string[]>([]);
  useEffect(() => { if (open && !dialog.current?.open) dialog.current?.showModal(); if (!open && dialog.current?.open) dialog.current.close(); }, [open]);
  useEffect(() => {
    if (!open) return;
    const nextTemplate = initialTemplate ?? "Talent & Hiring";
    setTemplate(nextTemplate);
    setTitle(nextTemplate);
    setObjective("");
  }, [initialTemplate, open]);
  useEffect(() => { if (open) setSelected(initialInvestigationId ? [initialInvestigationId] : []); }, [initialInvestigationId, open]);

  const ready = useMemo(() => readyMaterial(investigations), [investigations]);
  const suggested = useMemo(() => suggestedForTemplate(template, ready), [template, ready]);
  const suggestedIds = useMemo(() => new Set(suggested.map(item => item.id)), [suggested]);
  const other = ready.filter(item => !suggestedIds.has(item.id));
  const selectedIds = selected.filter(id => ready.some(item => item.id === id));
  const selectedMaterial = ready.filter(item => selectedIds.includes(item.id));
  const includedThrough = researchDate(selectedMaterial);
  const toggle = (id: string) => setSelected(current => current.includes(id) ? current.filter(value => value !== id) : [...current, id]);
  const changeTemplate = (value: BriefingTemplate) => {
    setTemplate(value);
    setTitle(current => !current.trim() || current === template ? value : current);
  };
  const renderItems = (items: Investigation[]) => <div className={styles.selectionList}>
    {items.map(item => <label key={item.id}>
      <input type="checkbox" checked={selected.includes(item.id)} onChange={() => toggle(item.id)} />
      <span><strong>{item.title}</strong>
        <small>{item.origin} · {item.purpose === "ProfileImprovement" ? "Profile improvement" : "General research"} · {item.topics.join(", ") || "Topic unspecified"} · {new Date(item.materialUpdatedAt).toLocaleDateString()}</small>
        {item.id === initialInvestigationId ? <small className={styles.currentInvestigation}>Added from the current Investigation</small> : null}
      </span>
    </label>)}
  </div>;

  return <dialog ref={dialog} className={styles.dialog} aria-labelledby="create-briefing-heading" onClose={onClose}>
    <form onSubmit={event => { event.preventDefault(); onCreate({ title: title.trim(), template, objective: objective.trim(), investigationIds: selectedIds }); }}>
      <header><div><p className={styles.dialogEyebrow}>CREATE BRIEFING</p><h2 id="create-briefing-heading">Create briefing</h2><p className={styles.dialogIntro}>Turn selected research into a durable thematic view. You choose what becomes part of this version; Briefings are not accepted Profile facts.</p></div><button type="button" onClick={onClose} aria-label="Close create briefing">×</button></header>

      <div className={styles.editorFocus}>
        <label htmlFor="briefing-template">Focus</label>
        <select id="briefing-template" value={template} onChange={event => changeTemplate(event.target.value as BriefingTemplate)}>
          {briefingTemplates.map(value => <option key={value}>{value}</option>)}
        </select>
        <small>Choose the thematic structure for the Briefing. You can edit the title and guidance below.</small>
      </div>

      <label htmlFor="briefing-title">Title<input id="briefing-title" required maxLength={200} value={title} onChange={event => setTitle(event.target.value)} placeholder={template} /></label>
      <label htmlFor="briefing-objective">Focus / additional guidance <span className={styles.optional}>Optional</span>
        <textarea id="briefing-objective" maxLength={2000} value={objective} onChange={event => setObjective(event.target.value)} placeholder="Example: Emphasize hiring trends, technical roles and geographic expansion." rows={3} />
        <small>If left blank, RAVEN uses the selected template’s standard focus.</small>
      </label>

      <fieldset>
        <legend>Research to include</legend>
        <p>RAVEN suggests relevant Investigations; you choose what becomes part of this Briefing.</p>
        {suggested.length ? <section><h3>Suggested for {template}</h3>{renderItems(suggested)}</section> : null}
        {other.length ? <section><h3>Other available research</h3>{renderItems(other)}</section> : null}
        {!ready.length ? <p>No ready or done Investigations are available yet.</p> : null}
      </fieldset>

      {error ? <p role="alert">{error}</p> : null}
      <footer><span className={styles.selectionSummary}><strong>{selectedIds.length} Investigation{selectedIds.length === 1 ? "" : "s"} selected</strong><small>{includedThrough ? `Research through ${includedThrough}` : "Select at least one Investigation"}</small></span><div><button className="button button--quiet" type="button" onClick={onClose}>Cancel</button><button className="button" type="submit" disabled={busy || selectedIds.length === 0 || !title.trim()}>{busy ? "Creating…" : "Create briefing"}</button></div></footer>
    </form>
  </dialog>;
}
