import { RefObject } from "react";
import { CaretLeft, Paperclip } from "@phosphor-icons/react";
import type { BriefingListItem } from "../../api/briefings";
import type { ManagedResearchJob, ResearchContextAttachment } from "../../api/managedResearch";
import styles from "./ask-raven.module.css";

interface Props {
  id: string;
  hasProfile: boolean;
  pickerOpen: boolean;
  onPickerOpen: (open: boolean) => void;
  search: string;
  onSearch: (value: string) => void;
  jobs: ManagedResearchJob[];
  briefings: BriefingListItem[];
  attachments: ResearchContextAttachment[];
  attachingId: string | null;
  webSearchEnabled: boolean;
  capabilitySaving: boolean;
  pending: boolean;
  conversationLoading: boolean;
  activeDeepResearch: boolean;
  firstActionRef: RefObject<HTMLButtonElement | null>;
  onAttachInvestigation: (job: ManagedResearchJob) => void;
  onAttachBriefing: (briefing: BriefingListItem) => void;
  onWebSearchChange: (enabled: boolean) => void;
  onDeepResearch: () => void;
}

export function AskRavenCapabilityMenu(props: Props) {
  const matchingJobs = props.jobs.filter((job) => job.status === "Completed" && job.investigationId && job.objective.toLowerCase().includes(props.search.toLowerCase()));
  const matchingBriefings = props.briefings.filter((briefing) => `${briefing.title} ${briefing.template}`.toLowerCase().includes(props.search.toLowerCase()));
  return <div aria-label="Additional capabilities" className={styles.capabilityPanel} id={props.id} role="group">
    {props.pickerOpen ? <>
      <div className={styles.evidencePickerHeader}>
        <button aria-label="Back to additional capabilities" className={styles.evidenceBackButton} type="button" onClick={() => props.onPickerOpen(false)}><CaretLeft size={14} aria-hidden="true" /> Back</button>
        <strong>Research context</strong>
        <span>Use completed Investigations and Briefings in this conversation.</span>
      </div>
      <section className={styles.evidencePickerPanel} aria-label="Choose research context">
        <input aria-label="Find research context" placeholder="Search research…" value={props.search} onChange={(event) => props.onSearch(event.target.value)} />
        <strong>Investigations</strong>
        <div className={styles.evidencePickerList}>
          {matchingJobs.length ? matchingJobs.map((job) => {
            const attached = props.attachments.some((item) => item.investigationId === job.investigationId);
            return <button className={styles.evidencePickerRow} key={job.id} type="button" onClick={() => props.onAttachInvestigation(job)} disabled={props.attachingId !== null || attached || props.attachments.length >= 5}>
              <span><b>{job.objective}</b><small>Deep Research · {job.completedAt ? new Date(job.completedAt).toLocaleDateString() : "Completed"}</small></span><em>{attached ? "✓ Added" : props.attachingId === job.id ? "Adding…" : "Add"}</em>
            </button>;
          }) : <p>No completed attachable Investigations found.</p>}
        </div>
        <strong>Briefings</strong>
        <div className={styles.evidencePickerList}>
          {matchingBriefings.length ? matchingBriefings.map((briefing) => {
            const attached = props.attachments.find((item) => item.kind === "Briefing" && item.briefingId === briefing.id);
            const current = attached?.briefingVersionNumber === briefing.versionNumber;
            return <button className={styles.evidencePickerRow} key={briefing.id} type="button" onClick={() => props.onAttachBriefing(briefing)} disabled={props.attachingId !== null || current || (!attached && props.attachments.length >= 5)}>
              <span><b>{briefing.title} · v{briefing.versionNumber}</b><small>Research through {new Date(briefing.researchThrough).toLocaleDateString()}</small></span><em>{current ? "✓ Added" : props.attachingId === `briefing:${briefing.id}` ? "Adding…" : attached ? `Update to v${briefing.versionNumber}` : "Add"}</em>
            </button>;
          }) : <p>No Briefings found.</p>}
        </div>
        {props.attachments.length >= 5 ? <small>Remove one context item to add another.</small> : null}
      </section>
    </> : <>
      <div className={styles.capabilityPanelHeader}><strong>Add to conversation</strong></div>
      <ul className={styles.capabilityList}>
        {props.hasProfile ? <li><button aria-label="Add research context" className={styles.capabilityAction} ref={props.firstActionRef} type="button" onClick={() => { props.onSearch(""); props.onPickerOpen(true); }}>
          <span className={styles.evidenceActionLabel}><Paperclip size={14} aria-hidden="true" /> Research context{props.attachments.length ? ` (${props.attachments.length}/5)` : ""}</span><small>Investigations &amp; Briefings</small>
        </button></li> : null}
        <li><label className={styles.capabilityToggle}><span><strong>Web search</strong><small>{props.capabilitySaving ? "Saving preference…" : props.webSearchEnabled ? "On · current public information" : "Off · current public information"}</small></span>
          <input aria-label="Web search" checked={props.webSearchEnabled} disabled={!props.hasProfile || props.pending || props.conversationLoading || props.capabilitySaving} onChange={(event) => props.onWebSearchChange(event.target.checked)} type="checkbox" />
        </label></li>
        <li><button aria-pressed={props.activeDeepResearch} className={`${styles.capabilityAction} ${props.activeDeepResearch ? styles.capabilitySelected : ""}`} type="button" disabled={!props.hasProfile} onClick={props.onDeepResearch}>
          <span>Deep Research</span><small>{props.hasProfile ? "Investigate a question in depth" : "Accept a profile to use in chat"}</small>
        </button></li>
      </ul>
    </>}
  </div>;
}
