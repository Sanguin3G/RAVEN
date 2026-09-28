import { CaretLeft, MagnifyingGlass, X } from "@phosphor-icons/react";
import type { BriefingListItem } from "../../api/briefings";
import type { ResearchContextAttachment } from "../../api/managedResearch";
import type { Investigation } from "../../api/investigations";
import styles from "./research-context-picker.module.css";

interface Props {
  search: string;
  investigations: Investigation[];
  briefings: BriefingListItem[];
  attachments: ResearchContextAttachment[];
  attachingId: string | null;
  onSearch: (value: string) => void;
  onBack: () => void;
  onAttachInvestigation: (investigation: Investigation) => void;
  onAttachBriefing: (briefing: BriefingListItem) => void;
  onRemove: (attachment: ResearchContextAttachment) => void;
}

function dateLabel(value?: string | null) {
  if (!value || Number.isNaN(Date.parse(value))) return "Completed";
  return new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short" }).format(Date.parse(value));
}

export function ResearchContextPicker({ search, investigations, briefings, attachments, attachingId, onSearch, onBack, onAttachInvestigation, onAttachBriefing, onRemove }: Props) {
  const query = search.trim().toLocaleLowerCase();
  const matchingInvestigations = investigations.filter((item) => (item.status === "Ready" || item.status === "Done") &&
    `${item.title} ${item.objective} ${item.summary}`.toLocaleLowerCase().includes(query));
  const matchingBriefings = briefings.filter((briefing) => `${briefing.title} ${briefing.template}`.toLocaleLowerCase().includes(query));

  return <section className={styles.picker} aria-label="Research context picker">
    <header className={styles.header}>
      <button aria-label="Back to additional capabilities" type="button" onClick={onBack}><CaretLeft size={14} aria-hidden="true" /> Back</button>
      <strong>Research context</strong>
      <small>Use saved Investigations and Briefings in this conversation.</small>
    </header>

    <label className={styles.search}>
      <MagnifyingGlass size={14} aria-hidden="true" />
      <span className="sr-only">Search Investigations and Briefings</span>
      <input placeholder="Search Investigations and Briefings…" value={search} onChange={(event) => onSearch(event.target.value)} />
      {search ? <button type="button" aria-label="Clear search" onClick={() => onSearch("")}><X size={13} aria-hidden="true" /></button> : null}
    </label>

    <div className={styles.scrollArea}>
      {attachments.length ? <section className={styles.attached} aria-label={`Attached research context, ${attachments.length} of 5`}>
        <strong>Attached · {attachments.length}/5</strong>
        {attachments.map((attachment) => <div className={styles.attachedRow} key={attachment.id}>
          <span aria-hidden="true">✓</span>
          <span title={attachment.title ?? attachment.objective ?? undefined}>{attachment.kind === "Briefing" ? `${attachment.title} · v${attachment.briefingVersionNumber}` : attachment.objective}</span>
          <button type="button" aria-label={`Remove ${attachment.title ?? attachment.objective ?? "research context"}`} onClick={() => onRemove(attachment)}>×</button>
        </div>)}
      </section> : null}

      <section className={styles.recent} aria-label="Recent research">
        <strong>Recent</strong>
        {matchingInvestigations.map((investigation) => {
          const attached = attachments.some((item) => item.investigationId === investigation.id);
          return <button className={styles.result} key={investigation.id} type="button" onClick={() => onAttachInvestigation(investigation)} disabled={attachingId !== null || attached || attachments.length >= 5}>
            <span className={styles.resultTitle}><b>{investigation.title}</b><small>{investigation.origin} · {dateLabel(investigation.materialUpdatedAt)}</small></span>
            <em>{attached ? "✓ Added" : attachingId === investigation.id ? "Adding…" : "Add"}</em>
          </button>;
        })}
        {matchingInvestigations.length && matchingBriefings.length ? <hr /> : null}
        {matchingBriefings.map((briefing) => {
          const attached = attachments.find((item) => item.kind === "Briefing" && item.briefingId === briefing.id);
          const current = attached?.briefingVersionNumber === briefing.versionNumber;
          return <button className={styles.result} key={briefing.id} type="button" onClick={() => onAttachBriefing(briefing)} disabled={attachingId !== null || current || (!attached && attachments.length >= 5)}>
            <span className={styles.resultTitle}><b>{briefing.title} · v{briefing.versionNumber}</b><small>Briefing · Research through {dateLabel(briefing.researchThrough)}</small></span>
            <em>{current ? "✓ Added" : attachingId === `briefing:${briefing.id}` ? "Adding…" : attached ? `Update to v${briefing.versionNumber}` : "Add"}</em>
          </button>;
        })}
        {!matchingInvestigations.length && !matchingBriefings.length ? <p>No matching research found.</p> : null}
      </section>
    </div>
    {attachments.length >= 5 ? <small className={styles.limit}>Remove one item before adding another.</small> : null}
  </section>;
}
