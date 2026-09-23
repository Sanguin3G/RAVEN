import { useEffect, useMemo, useRef, useState } from "react";
import type { Investigation } from "../../api/investigations";
import { briefingTemplates, suggestedForTemplate, type BriefingTemplate } from "../../api/briefings";
import styles from "./company-briefings.module.css";

interface Props {
  open: boolean; investigations: Investigation[]; initialInvestigationId?: string | null;
  busy: boolean; error?: string | null; onClose: () => void;
  onCreate: (value: { title: string; template: BriefingTemplate; objective: string; investigationIds: string[] }) => void;
}

export function BriefingEditor({ open, investigations, initialInvestigationId, busy, error, onClose, onCreate }: Props) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [template, setTemplate] = useState<BriefingTemplate>("Talent & Hiring");
  const [title, setTitle] = useState("");
  const [objective, setObjective] = useState("");
  const [selected, setSelected] = useState<string[]>([]);
  useEffect(() => { if (open && !dialog.current?.open) dialog.current?.showModal(); if (!open && dialog.current?.open) dialog.current.close(); }, [open]);
  useEffect(() => { if (open) setSelected(initialInvestigationId ? [initialInvestigationId] : []); }, [initialInvestigationId, open]);
  const options = useMemo(() => suggestedForTemplate(template, investigations), [template, investigations]);
  const toggle = (id: string) => setSelected(current => current.includes(id) ? current.filter(value => value !== id) : [...current, id]);
  return <dialog ref={dialog} className={styles.dialog} aria-labelledby="create-briefing-heading" onClose={onClose}>
    <form onSubmit={event => { event.preventDefault(); onCreate({ title: title.trim(), template, objective: objective.trim(), investigationIds: selected }); }}>
      <header><h2 id="create-briefing-heading">Create briefing</h2><button type="button" onClick={onClose} aria-label="Close create briefing">×</button></header>
      <label>Topic / template<select value={template} onChange={event => { const value = event.target.value as BriefingTemplate; setTemplate(value); if (!title) setTitle(value); }}>
        {briefingTemplates.map(value => <option key={value}>{value}</option>)}
      </select></label>
      <label>Title<input required maxLength={200} value={title} onChange={event => setTitle(event.target.value)} placeholder={template} /></label>
      <label>Objective / additional instruction<textarea required maxLength={2000} value={objective} onChange={event => setObjective(event.target.value)} placeholder="What should this Briefing focus on?" rows={3} /></label>
      <fieldset><legend>Choose Investigations</legend><p>Suggested by topic. Select only the material you want included.</p>
        {options.length ? <div className={styles.selectionList}>{options.map(item => <label key={item.id}><input type="checkbox" checked={selected.includes(item.id)} onChange={() => toggle(item.id)} /><span><strong>{item.title}</strong><small>{item.origin} · {item.topics.join(", ") || "Topic unspecified"} · {new Date(item.materialUpdatedAt).toLocaleDateString()}</small></span></label>)}</div> : <p>No ready or done Investigations are available yet.</p>}
      </fieldset>
      {error ? <p role="alert">{error}</p> : null}
      <footer><button className="button button--quiet" type="button" onClick={onClose}>Cancel</button><button className="button" type="submit" disabled={busy || selected.length === 0 || !title.trim() || !objective.trim()}>{busy ? "Creating…" : "Create briefing"}</button></footer>
    </form>
  </dialog>;
}
