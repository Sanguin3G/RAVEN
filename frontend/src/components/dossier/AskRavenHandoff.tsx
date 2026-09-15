import { FormEvent, useState } from "react";
import { ArrowUp } from "@phosphor-icons/react";
import { createChatConversation, sendChatMessage } from "../../api/chat";
import type { ChatAnswerStatus, ChatMessage } from "../../types/chat";
import styles from "./dossier.module.css";

export interface AskRavenHandoffProps {
  companyId: string;
  companyName: string;
  profileVersion?: number | null;
  profileVersionId?: string | null;
  sourceCount: number;
  lastResearchedAt?: string | null;
}

function formatDate(value?: string | null): string | null {
  if (!value) return null;
  const timestamp = Date.parse(value);
  return Number.isNaN(timestamp) ? null : new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short" }).format(timestamp);
}

const statusLabels: Record<ChatAnswerStatus, string> = {
  Answered: "Answered",
  ClarificationRequired: "Clarification needed",
  InsufficientEvidence: "Insufficient profile evidence",
  UnsupportedScope: "Outside current company scope",
};

export function AskRavenHandoff({ companyId, companyName, profileVersion, profileVersionId, sourceCount, lastResearchedAt }: AskRavenHandoffProps) {
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [question, setQuestion] = useState("");
  const [webSearchEnabled, setWebSearchEnabled] = useState(false);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const researched = formatDate(lastResearchedAt);

  const submitQuestion = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const trimmed = question.trim();
    if (!trimmed || pending || !profileVersionId) return;

    setPending(true);
    setError(null);
    const userMessage: ChatMessage = {
      id: `local-user-${Date.now()}`,
      role: "User",
      content: trimmed,
      status: "Completed",
      citations: [],
      toolExecutions: [],
      createdAt: new Date().toISOString(),
    };
    setMessages((current) => [...current, userMessage]);
    setQuestion("");

    try {
      const activeConversationId = conversationId ?? (await createChatConversation(companyId)).id;
      setConversationId(activeConversationId);
      const response = await sendChatMessage(companyId, activeConversationId, { question: trimmed });
      setMessages((current) => [...current, {
        id: response.messageId,
        role: "Assistant",
        content: response.answer,
        status: "Completed",
        answerStatus: response.status,
        followUpQuestion: response.followUpQuestion,
        citations: response.citations,
        toolExecutions: response.toolExecutions,
        createdAt: new Date().toISOString(),
      }]);
    } catch (submissionError) {
      setError(submissionError instanceof Error ? submissionError.message : "Ask RAVEN could not complete this request.");
    } finally {
      setPending(false);
    }
  };

  return (
    <section className={styles.handoff} data-testid="ask-raven-handoff" aria-labelledby="ask-raven-heading">
      <header className={styles.assistantCompactHeader}>
        <span aria-hidden="true" className={styles.aiMark}>AI</span>
        <div>
          <h2 id="ask-raven-heading">Ask RAVEN</h2>
          <p title={companyId}>{companyName}<span aria-hidden="true"> · </span>{profileVersion ? `v${profileVersion}` : "No profile"}<span aria-hidden="true"> · </span>{sourceCount} sources{researched ? <><span aria-hidden="true"> · </span>{researched}</> : null}</p>
        </div>
        <span className={styles.handoffStatus}>{pending ? "Thinking" : profileVersionId ? "Profile only" : "Profile required"}</span>
      </header>

      <div className={styles.chatViewport} aria-live="polite" aria-label="Ask RAVEN conversation">
        {messages.length === 0 ? <div className={styles.chatEmptyState}>
          <span className={styles.chatEmptyMark} aria-hidden="true">✦</span>
          <strong>{profileVersionId ? "Ask about this company" : "Accept a profile first"}</strong>
          <p>{profileVersionId ? "Answers are grounded in the accepted profile and its evidence." : "Ask RAVEN becomes available after a company profile is accepted."}</p>
        </div> : messages.map((message) => (
          <article key={message.id} className={`${styles.chatMessage} ${message.role === "User" ? styles.chatMessageUser : styles.chatMessageAssistant}`}>
            <span className={styles.chatMessageRole}>{message.role === "User" ? "You" : "RAVEN"}</span>
            <p>{message.content}</p>
            {message.answerStatus ? <span className={styles.chatMessageStatus}>{statusLabels[message.answerStatus]}</span> : null}
            {message.followUpQuestion ? <p className={styles.chatFollowUp}>{message.followUpQuestion}</p> : null}
            {message.citations.length > 0 ? <div className={styles.chatCitations}>
              {message.citations.map((citation) => <a key={citation.sourceDocumentId} className={styles.chatCitation} href={citation.url} target="_blank" rel="noreferrer">{citation.title ?? citation.fieldPath ?? "Profile source"}</a>)}
            </div> : null}
          </article>
        ))}
        {pending ? <div className={styles.chatSystemMessage} role="status">RAVEN is checking the accepted profile…</div> : null}
        {error ? <div className={styles.chatSystemMessage} role="alert">{error}</div> : null}
      </div>

      <form className={styles.assistantComposer} onSubmit={submitQuestion}>
        <label className="sr-only" htmlFor="ask-raven-question">Ask about {companyName}</label>
        <textarea
          id="ask-raven-question"
          value={question}
          disabled={!profileVersionId || pending}
          onChange={(event) => setQuestion(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) {
              event.preventDefault();
              event.currentTarget.form?.requestSubmit();
            }
          }}
          placeholder={profileVersionId ? `Ask about ${companyName}…` : "Accept a profile to ask questions…"}
          rows={2}
        />
        <div className={styles.assistantComposerFooter}>
          <label className={styles.webSearchToggle}>
            <input
              aria-label="Web search"
              checked={webSearchEnabled}
              onChange={(event) => setWebSearchEnabled(event.target.checked)}
              type="checkbox"
            />
            <span>Web search</span>
            <small>{webSearchEnabled ? "On · UI preview" : "Off · UI preview"}</small>
          </label>
          <span className={styles.assistantProfileBoundary}>Profile v{profileVersion ?? "—"} · answers use the accepted profile</span>
          <button className={styles.assistantSubmit} type="submit" disabled={!question.trim() || pending || !profileVersionId} aria-label="Send question">
            <ArrowUp size={17} weight="bold" aria-hidden="true" />
          </button>
        </div>
      </form>
    </section>
  );
}
