import type { Dispatch, RefObject, SetStateAction } from "react";
import { ArrowDown } from "@phosphor-icons/react";
import type { ManagedResearchBriefPreview, ManagedResearchJob } from "../../api/managedResearch";
import type { ChatMessage } from "../../types/chat";
import type { AskRavenNextActionKind } from "./AskRavenNextActionReview";
import { AskRavenMessage } from "./AskRavenMessage";
import { AskRavenDeepResearchReview } from "./AskRavenDeepResearchReview";
import styles from "./ask-raven.module.css";

type NextActionReview = { kind: AskRavenNextActionKind; messageId: string; value: string };

type Props = {
  chatViewportRef: RefObject<HTMLDivElement | null>;
  updateLatestVisibility: () => void;
  messages: ChatMessage[];
  deepResearchBrief: ManagedResearchBriefPreview | null;
  deepResearchBriefLoading: boolean;
  profileVersionId?: string | null;
  profileVersion?: number | null;
  sourceCount: number;
  companyName: string;
  setQuestion: Dispatch<SetStateAction<string>>;
  questionInputRef: RefObject<HTMLTextAreaElement | null>;
  expandedSourceMessageId: string | null;
  setExpandedSourceMessageId: Dispatch<SetStateAction<string | null>>;
  editingMessageId: string | null;
  editingMessageText: string;
  setEditingMessageId: Dispatch<SetStateAction<string | null>>;
  setEditingMessageText: Dispatch<SetStateAction<string>>;
  submitEditedMessage: () => void;
  pending: boolean;
  retryMessage: (content: string) => void;
  beginEditingMessage: (message: ChatMessage) => void;
  nextActionReview: NextActionReview | null;
  setNextActionReview: Dispatch<SetStateAction<NextActionReview | null>>;
  nextActionBusy: boolean;
  confirmNextAction: () => Promise<void>;
  setCapabilitiesOpen: Dispatch<SetStateAction<boolean>>;
  setInvestigationPickerOpen: Dispatch<SetStateAction<boolean>>;
  webSearchEnabled: boolean;
  deepResearchBriefEditing: boolean;
  setDeepResearchBriefEditing: Dispatch<SetStateAction<boolean>>;
  setDeepResearchBrief: Dispatch<SetStateAction<ManagedResearchBriefPreview | null>>;
  deepResearchStarting: boolean;
  cancelDeepResearchBrief: () => void;
  startDeepResearchBrief: () => Promise<void>;
  managedResearchJobs: ManagedResearchJob[];
  conversationId: string | null;
  error: string | null;
  speechError: string | null;
  showLatest: boolean;
};

const starterPrompts = [
  "What does this company do?",
  "Who are its key leaders?",
  "Where does it operate?",
  "What changed recently?",
  "Show me the supporting sources.",
];

export function AskRavenConversation({ chatViewportRef, updateLatestVisibility, messages, deepResearchBrief, deepResearchBriefLoading, profileVersionId, profileVersion, sourceCount, companyName, setQuestion, questionInputRef, expandedSourceMessageId, setExpandedSourceMessageId, editingMessageId, editingMessageText, setEditingMessageId, setEditingMessageText, submitEditedMessage, pending, retryMessage, beginEditingMessage, nextActionReview, setNextActionReview, nextActionBusy, confirmNextAction, setCapabilitiesOpen, setInvestigationPickerOpen, webSearchEnabled, deepResearchBriefEditing, setDeepResearchBriefEditing, setDeepResearchBrief, deepResearchStarting, cancelDeepResearchBrief, startDeepResearchBrief, managedResearchJobs, conversationId, error, speechError, showLatest }: Props) {
  return (
      <div className={styles.chatViewportShell}>
      <div ref={chatViewportRef} className={styles.chatViewport} aria-live="polite" aria-label="Ask RAVEN conversation" onScroll={updateLatestVisibility}>
        {messages.length === 0 && !deepResearchBrief && !deepResearchBriefLoading ? <div className={styles.chatEmptyState}>
          <span className={styles.chatEmptyMark} aria-hidden="true">✦</span>
          <strong>{profileVersionId ? "Ask about this company" : "Accept a profile first"}</strong>
          <p>{profileVersionId ? "Answers are grounded in the accepted profile and its evidence." : "Ask RAVEN and Deep Research in chat require an accepted company profile."}</p>
          {profileVersionId ? <div className={styles.starterList} aria-label="Suggested questions">
            {starterPrompts.map((prompt) => <button className={styles.starterPrompt} key={prompt} type="button" onClick={() => { setQuestion(prompt); questionInputRef.current?.focus(); }}>{prompt}</button>)}
          </div> : null}
        </div> : messages.map((message, messageIndex) => (
          <AskRavenMessage key={message.id} message={message} messageIndex={messageIndex} messages={messages} companyName={companyName} webSearchEnabled={webSearchEnabled} expandedSourceMessageId={expandedSourceMessageId} setExpandedSourceMessageId={setExpandedSourceMessageId} editingMessageId={editingMessageId} editingMessageText={editingMessageText} setEditingMessageId={setEditingMessageId} setEditingMessageText={setEditingMessageText} submitEditedMessage={submitEditedMessage} pending={pending} retryMessage={retryMessage} beginEditingMessage={beginEditingMessage} nextActionReview={nextActionReview} setNextActionReview={setNextActionReview} nextActionBusy={nextActionBusy} confirmNextAction={confirmNextAction} setCapabilitiesOpen={setCapabilitiesOpen} setInvestigationPickerOpen={setInvestigationPickerOpen} />
        ))}
        {deepResearchBriefLoading ? <div className={styles.researchBriefPreparing} role="status">Preparing research question…</div> : null}
{deepResearchBrief ? <AskRavenDeepResearchReview deepResearchBrief={deepResearchBrief} deepResearchBriefEditing={deepResearchBriefEditing} setDeepResearchBriefEditing={setDeepResearchBriefEditing} setDeepResearchBrief={setDeepResearchBrief} deepResearchStarting={deepResearchStarting} companyName={companyName} profileVersionId={profileVersionId} profileVersion={profileVersion} sourceCount={sourceCount} cancelDeepResearchBrief={cancelDeepResearchBrief} startDeepResearchBrief={startDeepResearchBrief} /> : null}
        {pending ? <div className={styles.chatSystemMessage} role="status">{[...messages].reverse().find((message) => message.role === "Assistant" && message.status === "Pending")?.activity ?? "RAVEN is preparing an answer…"}</div> : null}
        {deepResearchStarting ? <div className={styles.chatSystemMessage} role="status">Starting Deep Research in the background...</div> : null}
        {managedResearchJobs.some((job) => job.answerInChat && job.conversationId === conversationId && (job.status === "Queued" || job.status === "Researching")) ? <div className={styles.chatSystemMessage} role="status">Deep Research is running. RAVEN will answer from the Investigation when it is ready.</div> : null}
        {error ? <div className={styles.chatSystemMessage} role="alert">{error}</div> : null}
        {speechError ? <div className={styles.chatSystemMessage} role="alert">{speechError}</div> : null}
      </div>
      {showLatest ? <button type="button" className={styles.latestButton} onClick={() => chatViewportRef.current?.scrollTo({ top: chatViewportRef.current.scrollHeight, behavior: "smooth" })}><ArrowDown size={14} aria-hidden="true" /> Latest</button> : null}
      </div>
  );
}
