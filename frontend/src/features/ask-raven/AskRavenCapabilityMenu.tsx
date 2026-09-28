import { RefObject } from "react";
import { Paperclip } from "@phosphor-icons/react";
import type { BriefingListItem } from "../../api/briefings";
import type { ResearchContextAttachment } from "../../api/managedResearch";
import type { Investigation } from "../../api/investigations";
import { ResearchContextPicker } from "./ResearchContextPicker";
import styles from "./ask-raven.module.css";

interface Props {
  id: string;
  hasProfile: boolean;
  pickerOpen: boolean;
  onPickerOpen: (open: boolean) => void;
  search: string;
  onSearch: (value: string) => void;
  investigations: Investigation[];
  briefings: BriefingListItem[];
  attachments: ResearchContextAttachment[];
  attachingId: string | null;
  webSearchEnabled: boolean;
  capabilitySaving: boolean;
  pending: boolean;
  conversationLoading: boolean;
  activeDeepResearch: boolean;
  firstActionRef: RefObject<HTMLButtonElement | null>;
  onAttachInvestigation: (investigation: Investigation) => void;
  onAttachBriefing: (briefing: BriefingListItem) => void;
  onRemoveAttachment: (attachment: ResearchContextAttachment) => void;
  onWebSearchChange: (enabled: boolean) => void;
  onDeepResearch: () => void;
}

export function AskRavenCapabilityMenu(props: Props) {
  return <div aria-label="Additional capabilities" className={styles.capabilityPanel} id={props.id} role="group">
    {props.pickerOpen ? <ResearchContextPicker search={props.search} investigations={props.investigations} briefings={props.briefings}
      attachments={props.attachments} attachingId={props.attachingId} onSearch={props.onSearch}
      onBack={() => props.onPickerOpen(false)} onAttachInvestigation={props.onAttachInvestigation}
      onAttachBriefing={props.onAttachBriefing} onRemove={props.onRemoveAttachment} /> : <>
      <div className={styles.capabilityPanelHeader}><strong>Add to conversation</strong></div>
      <ul className={styles.capabilityList}>
        {props.hasProfile ? <li><button aria-label="Add research context" className={styles.capabilityAction} ref={props.firstActionRef} type="button" onClick={() => { props.onSearch(""); props.onPickerOpen(true); }}>
          <span className={styles.researchContextActionLabel}><Paperclip size={14} aria-hidden="true" /> Research context{props.attachments.length ? ` (${props.attachments.length}/5)` : ""}</span><small>Investigations &amp; Briefings</small>
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
