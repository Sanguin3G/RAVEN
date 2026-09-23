import { useEffect, useMemo, useRef, useState } from "react";
import type { Investigation, InvestigationStatus } from "../../api/investigations";
import { investigationPurposeLabel, investigationStatusLabel } from "./investigationTypes";
import styles from "./investigation-switcher.module.css";

interface Props { items: Investigation[]; selectedId: string | null; onSelect: (id: string) => void }
type Filter = InvestigationStatus | "All";
const groups: InvestigationStatus[] = ["Ready", "Running", "Done", "Failed"];

export function InvestigationSwitcher({ items, selectedId, onSelect }: Props) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [filter, setFilter] = useState<Filter>("All");
  const [showFilters, setShowFilters] = useState(false);
  const [purpose, setPurpose] = useState("All");
  const [topic, setTopic] = useState("All");
  const [origin, setOrigin] = useState("All");
  const trigger = useRef<HTMLButtonElement>(null);
  const search = useRef<HTMLInputElement>(null);
  const panel = useRef<HTMLDivElement>(null);
  const selected = items.find(item => item.id === selectedId);
  const counts = Object.fromEntries(groups.map(status => [status, items.filter(item => item.status === status).length]));
  const topics = [...new Set(items.flatMap(item => item.topics))].sort();
  const origins = [...new Set(items.map(item => item.origin))].sort();

  useEffect(() => { if (open) search.current?.focus(); }, [open]);
  useEffect(() => {
    if (!open) return;
    const outside = (event: PointerEvent) => {
      if (!panel.current?.contains(event.target as Node) && !trigger.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener("pointerdown", outside);
    return () => document.removeEventListener("pointerdown", outside);
  }, [open]);

  const visible = useMemo(() => items.filter(item =>
    (filter === "All" || item.status === filter) &&
    (purpose === "All" || item.purpose === purpose) &&
    (topic === "All" || item.topics.includes(topic)) &&
    (origin === "All" || item.origin === origin) &&
    `${item.title} ${item.objective} ${item.topics.join(" ")}`.toLowerCase().includes(query.toLowerCase().trim())
  ), [filter, items, origin, purpose, query, topic]);

  const choose = (id: string) => { onSelect(id); setOpen(false); trigger.current?.focus(); };
  return <div className={styles.switcher} onKeyDown={event => {
    if (event.key === "Escape" && open) { event.preventDefault(); setOpen(false); trigger.current?.focus(); }
  }}>
    <button ref={trigger} className={styles.trigger} type="button" aria-expanded={open} aria-controls="investigation-switcher-panel" onClick={() => setOpen(value => !value)}>
      <span className={styles.viewingLabel}>Viewing investigation</span><span className={styles.triggerChevron} aria-hidden="true">⌄</span>
      <strong className={styles.triggerTitle}>{selected?.title || "Select investigation"}</strong>
      {selected ? <small className={styles.triggerMeta}>{selected.origin} · {investigationPurposeLabel(selected.purpose)} · Updated {new Date(selected.materialUpdatedAt).toLocaleDateString()}</small> : null}
      {selected ? <span className={`${styles.triggerStatus} ${styles[`status${selected.status}`]}`}>{investigationStatusLabel(selected.status)}</span> : null}
    </button>
    {open ? <div ref={panel} id="investigation-switcher-panel" className={styles.panel} role="dialog" aria-label="Switch investigation">
      <label htmlFor="investigation-search">Search investigations</label>
      <input ref={search} id="investigation-search" type="search" value={query} onChange={event => setQuery(event.target.value)} placeholder="Search investigations..." />
      <div className={styles.filterRow}>
        <div className={styles.filters} aria-label="Filter by status">{(["Ready", "Running", "Done", "Failed", "All"] as Filter[]).map(value =>
          <button type="button" key={value} className={filter === value ? styles.active : ""} aria-pressed={filter === value} onClick={() => setFilter(value)}>{value === "Ready" ? "Ready" : value} {value === "All" ? items.length : counts[value]}</button>)}</div>
        <button type="button" className={styles.filterToggle} aria-expanded={showFilters} onClick={() => setShowFilters(value => !value)}>{showFilters ? "Hide filters" : "Filters"}</button>
      </div>
      {showFilters ? <div className={styles.advanced}>
          <label>Purpose <select value={purpose} onChange={event => setPurpose(event.target.value)}><option value="All">All</option><option value="GeneralResearch">General research</option><option value="ProfileImprovement">Profile improvement</option></select></label>
          <label>Topic <select value={topic} onChange={event => setTopic(event.target.value)}><option value="All">All</option>{topics.map(value => <option key={value}>{value}</option>)}</select></label>
          <label>Origin <select value={origin} onChange={event => setOrigin(event.target.value)}><option value="All">All</option>{origins.map(value => <option key={value}>{value}</option>)}</select></label>
        </div> : null}
      {visible.length ? <div className={styles.results}>{groups.map(group => {
        const groupItems = visible.filter(item => item.status === group);
        return groupItems.length ? <section key={group} aria-label={investigationStatusLabel(group)}><h3>{investigationStatusLabel(group)}</h3><ul>{groupItems.map(item =>
          <li key={item.id}><button type="button" aria-current={item.id === selectedId ? "true" : undefined} onClick={() => choose(item.id)}><strong>{item.title}</strong><small>{item.origin}</small><small>{investigationPurposeLabel(item.purpose)} · {item.topics[0] || "Topic unspecified"} · {new Date(item.materialUpdatedAt).toLocaleDateString()}</small></button></li>)}</ul></section> : null;
      })}</div> : <p role="status">No investigations match these filters.</p>}
    </div> : null}
  </div>;
}
