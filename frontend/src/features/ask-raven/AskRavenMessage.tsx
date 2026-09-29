import type { Dispatch, SetStateAction } from "react";
import { CaretDown } from "@phosphor-icons/react";
import type { ChatMessage } from "../../types/chat";
import { AskRavenNextActionReview, type AskRavenNextActionKind } from "./AskRavenNextActionReview";
import { AskRavenMessageActions, AskRavenUserMessageActions } from "./AskRavenMessageActions";
import styles from "./ask-raven.module.css";

type NextActionReview = { kind: AskRavenNextActionKind; messageId: string; value: string };

type Props = {
  message: ChatMessage;
  messageIndex: number;
  messages: ChatMessage[];
  companyName: string;
  webSearchEnabled: boolean;
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
};

function sourceDomain(url: string) {
  try { return new URL(url).hostname; } catch { return url; }
}

export function AskRavenMessage({ message, messageIndex, messages, companyName, webSearchEnabled, expandedSourceMessageId, setExpandedSourceMessageId, editingMessageId, editingMessageText, setEditingMessageId, setEditingMessageText, submitEditedMessage, pending, retryMessage, beginEditingMessage, nextActionReview, setNextActionReview, nextActionBusy, confirmNextAction, setCapabilitiesOpen, setInvestigationPickerOpen }: Props) {
  return (
          <article className={`${styles.chatMessage} ${message.role === "User" ? `${styles.chatMessageUser} chat-message-user${editingMessageId === message.id ? ` ${styles.chatMessageEditing}` : ""}` : styles.chatMessageAssistant}`}>
            <span className={styles.chatMessageRole}>{message.role === "User" ? "You" : "RAVEN"}</span>
            {message.role === "User" && editingMessageId === message.id ? <div className={styles.inlineMessageEdit}>
              <label className="sr-only" htmlFor={`edit-${message.id}`}>Edit message and send again</label>
              <textarea id={`edit-${message.id}`} autoFocus value={editingMessageText} onChange={(event) => setEditingMessageText(event.target.value)} rows={3} />
              <small>Sends as a new message; the original remains in history.</small>
              <div><button type="button" onClick={() => { setEditingMessageId(null); setEditingMessageText(""); }}>Cancel</button><button type="button" disabled={!editingMessageText.trim()} onClick={submitEditedMessage}>Send</button></div>
            </div> : message.content ? <p>{message.content}</p> : null}
            {message.followUpQuestion ? <p className={styles.chatFollowUp}>{message.followUpQuestion}</p> : null}
            {message.citations.length > 0 ? <>
              <button className={styles.sourceToggle} type="button" aria-expanded={expandedSourceMessageId === message.id} onClick={() => setExpandedSourceMessageId((current) => current === message.id ? null : message.id)}>
                <span>Sources</span><CaretDown className={styles.sourceChevron} size={13} aria-hidden="true" />
              </button>
              {expandedSourceMessageId === message.id ? <section className={styles.sourceList} aria-label="Sources used for this answer">
                {message.citations.map((citation) => <a key={citation.briefingVersionId ?? citation.investigationId ?? citation.webEvidenceSnapshotId ?? citation.sourceDocumentId ?? citation.url} className={styles.sourceItem} href={citation.url} target={citation.origin === "Investigation" || citation.origin === "Briefing" ? undefined : "_blank"} rel={citation.origin === "Investigation" || citation.origin === "Briefing" ? undefined : "noreferrer"}>
                  <span>{citation.origin === "Investigation" ? "Investigation" : citation.origin === "Briefing" ? "Briefing" : citation.origin === "Web" ? "Web source" : "Profile source"}</span>
                  <strong>{citation.title ?? citation.fieldPath ?? sourceDomain(citation.url)}</strong>
                  {citation.origin === "Web" || citation.origin === "Profile" ? <small>{sourceDomain(citation.url)}</small> : null}
                </a>)}
              </section> : null}
            </> : null}
            {message.role === "Assistant" && message.status === "Completed" ? <div className={styles.messageNextActions} aria-label="Continue this answer">
              {message.citations.some((citation) => citation.origin === "Briefing") ? <a href={message.citations.find((citation) => citation.origin === "Briefing")?.url}>Open briefing</a> : null}
              {message.answerStatus === "InsufficientEvidence" || message.answerStatus === "ClarificationRequired" ? <button type="button" onClick={() => { setCapabilitiesOpen(true); setInvestigationPickerOpen(true); }}>Add research context</button> : null}
              {!webSearchEnabled && message.answerStatus === "InsufficientEvidence" ? <button type="button" onClick={() => {
                const priorQuestion = messages.slice(0, messageIndex).reverse().find((item) => item.role === "User")?.content;
                const related = (priorQuestion || `company updates for ${companyName}`).trim().slice(0, 3_500);
                setNextActionReview({ kind: "latest", messageId: message.id, value: `Search current public information about: ${related}` });
              }}>Search the web</button> : null}
              {message.citations.some((citation) => citation.origin === "Briefing" || citation.origin === "Web") ? <button type="button" onClick={() => {
                const priorQuestion = messages.slice(0, messageIndex).reverse().find((item) => item.role === "User")?.content;
                const related = (priorQuestion || `the current research about ${companyName}`).trim().slice(0, 3_500);
                setNextActionReview({ kind: "research", messageId: message.id, value: `Investigate in depth: ${related}` });
              }}>Research further</button> : null}
              {message.answerStatus === "Answered" && !message.citations.some((citation) => citation.origin === "Briefing" || citation.origin === "Web") ? <button type="button" onClick={() => {
                const priorQuestion = messages.slice(0, messageIndex).reverse().find((item) => item.role === "User")?.content;
                const related = (priorQuestion || `company updates for ${companyName}`).trim().slice(0, 3_500);
                setNextActionReview({ kind: "latest", messageId: message.id, value: `Search current public information about: ${related}` });
              }}>Search latest</button> : null}
            </div> : null}
            {nextActionReview?.messageId === message.id ? <AskRavenNextActionReview kind={nextActionReview.kind} value={nextActionReview.value} busy={nextActionBusy}
              onChange={(value) => setNextActionReview((current) => current?.messageId === message.id ? { ...current, value } : current)}
              onCancel={() => setNextActionReview(null)} onConfirm={() => void confirmNextAction()} /> : null}
            {message.role === "Assistant" && message.status === "Completed" ? <AskRavenMessageActions messageId={message.id} content={message.content} createdAt={message.createdAt} disabled={pending}
              onRetry={() => retryMessage(messages.slice(0, messageIndex).reverse().find((item) => item.role === "User")?.content ?? "")} /> : null}
            {message.role === "User" && editingMessageId !== message.id ? <AskRavenUserMessageActions content={message.content} createdAt={message.createdAt} disabled={pending}
              onRetry={() => retryMessage(message.content)} onEdit={() => beginEditingMessage(message)} /> : null}
          </article>
  );
}
