import { useEffect, useRef, useState } from "react";
import type { Briefing } from "../../api/briefings";
import styles from "./company-briefings.module.css";

interface Props {
  briefing: Briefing; mode: "latest" | "gaps" | null; onClose: () => void;
  onDeepResearch?: (objective: string) => void; onExternalResearch?: (objective: string) => void;
}

export function BriefingResearchDialog({ briefing, mode, onClose, onDeepResearch, onExternalResearch }: Props) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [selected, setSelected] = useState<string[]>([]);
  const gaps = briefing.currentVersion.sections.filter(section => /uncertaint|open questions/i.test(section.title)).flatMap(section => section.items);
  const latest = [briefing.objective, ...briefing.currentVersion.sections.filter(section => /recent developments|open questions/i.test(section.title)).flatMap(section => section.items).slice(0, 4)];
  const options = mode === "gaps" ? gaps : latest;
  useEffect(() => { if (mode && !dialog.current?.open) dialog.current?.showModal(); if (!mode && dialog.current?.open) dialog.current.close(); }, [mode]);
  useEffect(() => { setSelected(mode === "latest" ? [briefing.objective] : []); }, [briefing.objective, mode]);
  const launch = (method: "deep" | "external") => {
    const objective = `${briefing.title}: ${selected.join("; ")}`;
    if (method === "deep") onDeepResearch?.(objective); else onExternalResearch?.(objective);
    onClose();
  };
  return <dialog ref={dialog} className={styles.dialog} aria-labelledby="briefing-research-heading" onClose={onClose}>
    <header><h2 id="briefing-research-heading">{mode === "gaps" ? "Research gaps" : "Research latest"}</h2><button type="button" aria-label="Close research handoff" onClick={onClose}>×</button></header>
    <p>Select a focus. Research runs through an existing Investigation path and returns to Investigations. Update briefing later to include the result.</p>
    {options.length ? <div className={styles.selectionList}>{options.map((value, index) => <label key={`${value}-${index}`}><input type="checkbox" checked={selected.includes(value)} onChange={() => setSelected(current => current.includes(value) ? current.filter(item => item !== value) : [...current, value])} /><span>{value}</span></label>)}</div> : <p>No focused questions were identified in this Briefing.</p>}
    <footer><button className="button button--quiet" type="button" onClick={onClose}>Cancel</button><button className="button button--secondary" type="button" disabled={!selected.length} onClick={() => launch("external")}>External AI Assist</button><button className="button" type="button" disabled={!selected.length} onClick={() => launch("deep")}>Deep Research</button></footer>
  </dialog>;
}
