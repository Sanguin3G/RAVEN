import type { FormEvent, RefObject, ReactNode } from "react";
import { ArrowUp, Plus, Stop } from "@phosphor-icons/react";
import type { ResearchContextAttachment } from "../../api/managedResearch";
import type { SpeechPreferences } from "./speechPreferences";
import { SpeechDeviceMenu } from "./SpeechDeviceMenu";
import styles from "./ask-raven.module.css";

export type ComposerCapability = "webSearch" | "deepResearch";

interface SpeechControls {
  supported: boolean;
  starting: boolean;
  listening: boolean;
  level: number;
  cancel: () => void;
  finish: () => void;
}

interface Props {
  companyName: string;
  question: string;
  onQuestionChange: (value: string) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void;
  onStop: () => void;
  questionInputRef: RefObject<HTMLTextAreaElement | null>;
  profileVersionId: string | null;
  pending: boolean;
  conversationLoading: boolean;
  deepResearchBriefActive: boolean;
  deepResearchBriefLoading: boolean;
  deepResearchStarting: boolean;
  activeCapability: ComposerCapability | null;
  onRemoveDeepResearch: () => void;
  webSearchEnabled: boolean;
  researchContextAttachments: ResearchContextAttachment[];
  contextDetailsOpen: boolean;
  onToggleContextDetails: () => void;
  onRemoveAttachment: (attachment: ResearchContextAttachment) => void;
  onClearAllAttachments: () => void;
  capabilitiesOpen: boolean;
  capabilitiesId: string;
  capabilitiesRef: RefObject<HTMLDivElement | null>;
  capabilitiesTriggerRef: RefObject<HTMLButtonElement | null>;
  onToggleCapabilities: () => void;
  capabilitiesMenu: ReactNode;
  speech: SpeechControls;
  onStartSpeech: (preferences: SpeechPreferences) => void;
}

export function AskRavenComposer(props: Props) {
  const isDeepResearch = props.activeCapability === "deepResearch";
  const questionDisabled = !props.profileVersionId || props.pending || props.deepResearchBriefLoading ||
    props.deepResearchStarting || props.conversationLoading || props.deepResearchBriefActive;

  return <form className={`${styles.assistantComposer} ${props.speech.listening ? styles.assistantComposerListening : ""}`} onSubmit={props.onSubmit}>
    <label className="sr-only" htmlFor="ask-raven-question">{isDeepResearch ? `Research about ${props.companyName}` : `Ask about ${props.companyName}`}</label>
    <textarea
      id="ask-raven-question"
      ref={props.questionInputRef}
      value={props.question}
      disabled={questionDisabled}
      onChange={(event) => props.onQuestionChange(event.target.value)}
      onKeyDown={(event) => {
        if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) {
          event.preventDefault();
          event.currentTarget.form?.requestSubmit();
        }
      }}
      placeholder={isDeepResearch
        ? `What would you like RAVEN to investigate about ${props.companyName}?`
        : props.profileVersionId ? `Ask about ${props.companyName}…` : "Accept a profile to ask questions…"}
      rows={2}
    />
    {props.researchContextAttachments.length > 0 ? <div className={styles.researchContextAttachments} aria-label="Attached research context" role="group">
      <button type="button" className={styles.researchContextSummary} aria-expanded={props.contextDetailsOpen} onClick={props.onToggleContextDetails}>Research ×{props.researchContextAttachments.length}</button>
      {props.contextDetailsOpen ? props.researchContextAttachments.map((attachment) => <span className={styles.researchContextChip} key={attachment.id}>
        <span title={attachment.kind === "Briefing" ? attachment.title ?? undefined : attachment.objective ?? undefined}>✦ {attachment.kind === "Briefing" ? `Briefing · ${attachment.title} · v${attachment.briefingVersionNumber}` : `Investigation · ${attachment.objective}`}</span>
        <button type="button" onClick={() => props.onRemoveAttachment(attachment)} aria-label={`Remove ${attachment.title ?? attachment.objective ?? "research"} context`}>×</button>
      </span>) : null}
      {props.contextDetailsOpen && props.researchContextAttachments.length > 1 ? <button type="button" className={styles.clearContextButton} onClick={props.onClearAllAttachments}>Clear all</button> : null}
    </div> : null}
    <div className={styles.assistantComposerFooter}>
      <div className={styles.capabilityControls} ref={props.capabilitiesRef}>
        <button
          aria-controls={props.capabilitiesId}
          aria-expanded={props.capabilitiesOpen}
          aria-label="Additional capabilities"
          className={styles.capabilityTrigger}
          ref={props.capabilitiesTriggerRef}
          type="button"
          onClick={props.onToggleCapabilities}
        >
          <Plus size={17} weight="bold" aria-hidden="true" />
        </button>
        {props.capabilitiesMenu}
      </div>
      {isDeepResearch ? <button className={styles.capabilityChip} type="button" onClick={props.onRemoveDeepResearch} aria-label="Remove Deep Research capability">✦ Deep Research ×</button> : null}
      {props.webSearchEnabled ? <span className={styles.capabilityChip}>Web</span> : null}
      <SpeechDeviceMenu supported={props.speech.supported} disabled={!props.profileVersionId || props.pending || props.conversationLoading || props.deepResearchBriefActive || props.speech.starting} onStart={props.onStartSpeech} />
      {props.pending ? <button className={`${styles.assistantSubmit} ${styles.assistantStop}`} type="button" onClick={props.onStop} aria-label="Stop response" title="Stop response">
        <Stop size={15} weight="fill" aria-hidden="true" />
      </button> : props.question.trim() ? <button className={styles.assistantSubmit} type="submit" disabled={!props.profileVersionId || props.deepResearchBriefLoading || props.deepResearchStarting || props.deepResearchBriefActive || props.conversationLoading} aria-label={isDeepResearch ? "Review research question" : "Send question"}>
        <ArrowUp size={17} weight="bold" aria-hidden="true" />
      </button> : null}
    </div>
    {props.speech.listening ? <div className={styles.listeningPanel} role="status">
      <strong>Listening…</strong>
      <div className={styles.speechLevel} aria-label="Live microphone level"><span style={{ width: `${Math.max(2, props.speech.level * 100)}%` }} /></div>
      <small>System default microphone</small>
      <div><button type="button" onClick={props.speech.cancel}>Cancel</button><button type="button" onClick={props.speech.finish}>Finish</button></div>
    </div> : null}
  </form>;
}
