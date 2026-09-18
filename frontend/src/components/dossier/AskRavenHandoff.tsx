import { FormEvent, useEffect, useId, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { ArrowUp, CaretDown, Plus } from "@phosphor-icons/react";
import { createChatConversation, getChatConversation, sendChatMessageStream, updateChatCapabilities } from "../../api/chat";
import type { ChatAnswerStatus, ChatMessage } from "../../types/chat";
import styles from "./AskRaven.module.css";

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
  Conversational: "Conversation",
  Guidance: "Guidance",
  ClarificationRequired: "Clarification needed",
  InsufficientEvidence: "Insufficient profile evidence",
  UnsupportedScope: "Outside current company scope",
};

type AnswerEvidenceMode = "Profile" | "Web" | "Mixed";

function getAnswerEvidenceMode(message: ChatMessage): AnswerEvidenceMode | null {
  if (message.role !== "Assistant" || !message.answerStatus) return null;
  const hasProfile = message.citations.some((citation) => citation.origin === "Profile");
  const hasWeb = message.citations.some((citation) => citation.origin === "Web");
  if (hasProfile && hasWeb) return "Mixed";
  if (hasProfile) return "Profile";
  if (hasWeb) return "Web";
  return null;
}

function sourceDomain(url: string) {
  try { return new URL(url).hostname; } catch { return url; }
}
const starterPrompts = [
  "What does this company do?",
  "Who are its key leaders?",
  "Where does it operate?",
  "What changed recently?",
  "Show me the supporting sources.",
];

export function AskRavenHandoff({ companyId, companyName, profileVersion, profileVersionId, sourceCount, lastResearchedAt }: AskRavenHandoffProps) {
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedConversationId = searchParams.get("conversation");
  const [conversationId, setConversationId] = useState<string | null>(requestedConversationId);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [question, setQuestion] = useState("");
  const [webSearchEnabled, setWebSearchEnabled] = useState(false);
  const [capabilitiesOpen, setCapabilitiesOpen] = useState(false);
  const [conversationLoading, setConversationLoading] = useState(false);
  const [capabilitySaving, setCapabilitySaving] = useState(false);
  const [pending, setPending] = useState(false);
  const [expandedSourceMessageId, setExpandedSourceMessageId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const capabilitiesId = useId();
  const capabilitiesRef = useRef<HTMLDivElement>(null);
  const capabilitiesTriggerRef = useRef<HTMLButtonElement>(null);
  const investigationsLinkRef = useRef<HTMLAnchorElement>(null);
  const questionInputRef = useRef<HTMLTextAreaElement>(null);
  const researched = formatDate(lastResearchedAt);

  const setConversationInUrl = (nextConversationId: string | null) => {
    const next = new URLSearchParams(searchParams);
    if (nextConversationId) next.set("conversation", nextConversationId);
    else next.delete("conversation");
    setSearchParams(next, { replace: true });
  };

  const startNewConversation = () => {
    if (pending || conversationLoading) return;
    setConversationId(null);
    setMessages([]);
    setQuestion("");
    setWebSearchEnabled(false);
    setCapabilitiesOpen(false);
    setExpandedSourceMessageId(null);
    setError(null);
    setConversationInUrl(null);
    questionInputRef.current?.focus();
  };

  const beginConversation = async (syncUrl = true) => {
    const created = await createChatConversation(companyId);
    setConversationId(created.id);
    setWebSearchEnabled(created.webSearchEnabled ?? false);
    if (syncUrl) setConversationInUrl(created.id);
    return created;
  };

  useEffect(() => {
    let active = true;
    if (!requestedConversationId) {
      setConversationId(null);
      setMessages([]);
      setWebSearchEnabled(false);
      setConversationLoading(false);
      return () => { active = false; };
    }

    setConversationLoading(true);
    setError(null);
    getChatConversation(companyId, requestedConversationId)
      .then((conversation) => {
        if (!active) return;
        setConversationId(conversation.id);
        setMessages(conversation.messages);
        setWebSearchEnabled(conversation.webSearchEnabled);
      })
      .catch((reason: unknown) => {
        if (!active) return;
        setConversationId(null);
        setMessages([]);
        setWebSearchEnabled(false);
        setConversationInUrl(null);
        setError(reason instanceof Error ? reason.message : "Could not restore this Ask RAVEN conversation.");
      })
      .finally(() => { if (active) setConversationLoading(false); });
    return () => { active = false; };
  }, [companyId, requestedConversationId]);

  const setWebSearchCapability = async (nextEnabled: boolean) => {
    if (!profileVersionId || pending || conversationLoading || capabilitySaving) return;

    setCapabilitySaving(true);
    setError(null);
    try {
      const activeConversation = conversationId ? { id: conversationId } : await beginConversation();
      const updated = await updateChatCapabilities(companyId, activeConversation.id, { webSearchEnabled: nextEnabled });
      setConversationId(updated.id);
      setMessages(updated.messages);
      setWebSearchEnabled(updated.webSearchEnabled);
      setConversationInUrl(updated.id);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Could not update Web Search for this conversation.");
    } finally {
      setCapabilitySaving(false);
    }
  };

  useEffect(() => {
    if (!capabilitiesOpen) return;

    const closeOnOutsidePointer = (event: PointerEvent) => {
      if (!capabilitiesRef.current?.contains(event.target as Node)) setCapabilitiesOpen(false);
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key !== "Escape") return;
      event.preventDefault();
      setCapabilitiesOpen(false);
      capabilitiesTriggerRef.current?.focus();
    };

    document.addEventListener("pointerdown", closeOnOutsidePointer);
    document.addEventListener("keydown", closeOnEscape);
    investigationsLinkRef.current?.focus();
    return () => {
      document.removeEventListener("pointerdown", closeOnOutsidePointer);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [capabilitiesOpen]);

  const submitQuestion = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const trimmed = question.trim();
    if (!trimmed || pending || !profileVersionId) return;

    const localUserId = `local-user-${Date.now()}`;
    const localAssistantId = `local-assistant-${Date.now()}`;
    setPending(true);
    setError(null);
    setMessages((current) => [...current,
      { id: localUserId, role: "User", content: trimmed, status: "Completed", citations: [], webEvidenceSnapshots: [], toolExecutions: [], createdAt: new Date().toISOString() },
      { id: localAssistantId, role: "Assistant", content: "", status: "Pending", activity: "Analyzing the question", citations: [], webEvidenceSnapshots: [], toolExecutions: [], createdAt: new Date().toISOString() },
    ]);
    setQuestion("");

    let activeConversationId = conversationId;
    try {
      // Do not update the URL yet. Its restore effect can return an empty
      // conversation before this first streamed turn has been persisted.
      activeConversationId ??= (await beginConversation(false)).id;
      let completed = false;
      await sendChatMessageStream(companyId, activeConversationId, { question: trimmed }, (streamEvent) => {
        if (streamEvent.type === "progress") {
          setMessages((current) => current.map((message) => message.id === localAssistantId ? { ...message, activity: streamEvent.completed !== null && streamEvent.total !== null ? `${streamEvent.message} (${streamEvent.completed}/${streamEvent.total})` : streamEvent.message } : message));
          return;
        }
        if (streamEvent.type === "completed") {
          completed = true;
          const response = streamEvent.response;
          setMessages((current) => current.map((message) => message.id === localAssistantId ? {
            id: response.messageId,
            role: "Assistant",
            content: response.answer,
            status: "Completed",
            activity: null,
            answerStatus: response.status,
            followUpQuestion: response.followUpQuestion,
            citations: response.citations,
            webEvidenceSnapshots: response.webEvidenceSnapshots,
            toolExecutions: response.toolExecutions,
            createdAt: new Date().toISOString(),
          } : message));
          return;
        }
        setMessages((current) => current.map((message) => message.id === localAssistantId ? { ...message, content: streamEvent.message, status: "Failed", activity: null } : message));
        throw new Error(streamEvent.message);
      });
      if (!completed) throw new Error("Ask RAVEN ended before returning a completed response.");
    } catch (submissionError) {
      setMessages((current) => current.map((message) => message.id === localAssistantId && message.status === "Pending" ? { ...message, content: "Ask RAVEN could not complete this response.", status: "Failed", activity: null } : message));
      setError(submissionError instanceof Error ? submissionError.message : "Ask RAVEN could not complete this request.");
    } finally {
      if (activeConversationId && !requestedConversationId) setConversationInUrl(activeConversationId);
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
        <button className={styles.newConversationButton} type="button" onClick={startNewConversation} disabled={pending || conversationLoading} aria-label="New conversation">
          <Plus size={14} weight="bold" aria-hidden="true" />
          <span>New chat</span>
        </button>
        <span className={styles.handoffStatus}>{pending ? "Thinking" : conversationLoading ? "Loading chat" : profileVersionId ? webSearchEnabled ? "Web permitted" : "Profile only" : "Profile required"}</span>
      </header>

      <div className={styles.chatViewport} aria-live="polite" aria-label="Ask RAVEN conversation">
        {messages.length === 0 ? <div className={styles.chatEmptyState}>
          <span className={styles.chatEmptyMark} aria-hidden="true">✦</span>
          <strong>{profileVersionId ? "Ask about this company" : "Accept a profile first"}</strong>
          <p>{profileVersionId ? "Answers are grounded in the accepted profile and its evidence." : "Ask RAVEN becomes available after a company profile is accepted."}</p>
          {profileVersionId ? <div className={styles.starterList} aria-label="Suggested questions">
            {starterPrompts.map((prompt) => <button className={styles.starterPrompt} key={prompt} type="button" onClick={() => { setQuestion(prompt); questionInputRef.current?.focus(); }}>{prompt}</button>)}
          </div> : null}
        </div> : messages.map((message) => (
          <article key={message.id} className={`${styles.chatMessage} ${message.role === "User" ? styles.chatMessageUser : styles.chatMessageAssistant}`}>
            <span className={styles.chatMessageRole}>{message.role === "User" ? "You" : "RAVEN"}</span>
            {message.content ? <p>{message.content}</p> : null}
            {message.answerStatus ? <div className={styles.chatMessageMeta}><span className={styles.chatMessageStatus}>{statusLabels[message.answerStatus]}</span>{getAnswerEvidenceMode(message) ? <span className={`${styles.answerEvidenceBadge} ${styles[`answerEvidenceBadge${getAnswerEvidenceMode(message)}`]}`}>{getAnswerEvidenceMode(message)}</span> : null}</div> : null}
            {message.followUpQuestion ? <p className={styles.chatFollowUp}>{message.followUpQuestion}</p> : null}
            {message.citations.length > 0 ? <>
              <button className={styles.sourceToggle} type="button" aria-expanded={expandedSourceMessageId === message.id} onClick={() => setExpandedSourceMessageId((current) => current === message.id ? null : message.id)}>
                <span>Nguồn</span><CaretDown className={styles.sourceChevron} size={13} aria-hidden="true" />
              </button>
              {expandedSourceMessageId === message.id ? <section className={styles.sourceList} aria-label="Sources used for this answer">
                {message.citations.map((citation) => <a key={citation.webEvidenceSnapshotId ?? citation.sourceDocumentId ?? citation.url} className={styles.sourceItem} href={citation.url} target="_blank" rel="noreferrer">
                  <span>{citation.origin === "Web" ? "Web source" : "Profile source"}</span>
                  <strong>{citation.title ?? citation.fieldPath ?? sourceDomain(citation.url)}</strong>
                  <small>{sourceDomain(citation.url)}</small>
                </a>)}
              </section> : null}
            </> : null}
          </article>
        ))}
        {pending ? <div className={styles.chatSystemMessage} role="status">{[...messages].reverse().find((message) => message.role === "Assistant" && message.status === "Pending")?.activity ?? "RAVEN is preparing an answer…"}</div> : null}
        {error ? <div className={styles.chatSystemMessage} role="alert">{error}</div> : null}
      </div>

      <form className={styles.assistantComposer} onSubmit={submitQuestion}>
        <label className="sr-only" htmlFor="ask-raven-question">Ask about {companyName}</label>
        <textarea
          id="ask-raven-question"
          ref={questionInputRef}
          value={question}
          disabled={!profileVersionId || pending || conversationLoading}
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
          <div className={styles.capabilityControls} ref={capabilitiesRef}>
            <button
              aria-controls={capabilitiesId}
              aria-expanded={capabilitiesOpen}
              aria-label="Additional capabilities"
              className={styles.capabilityTrigger}
              ref={capabilitiesTriggerRef}
              type="button"
              onClick={() => setCapabilitiesOpen((open) => !open)}
            >
              <Plus size={17} weight="bold" aria-hidden="true" />
            </button>
            {capabilitiesOpen ? <div aria-label="Additional capabilities" className={styles.capabilityPanel} id={capabilitiesId} role="group">
              <div className={styles.capabilityPanelHeader}>
                <strong>Additional capabilities</strong>
                <span>Choose a next step.</span>
              </div>
              <ul className={styles.capabilityList}>
                <li>
                  <a className={styles.capabilityAction} ref={investigationsLinkRef} href={`/companies/${encodeURIComponent(companyId)}?tab=investigations`} onClick={() => setCapabilitiesOpen(false)}>
                    <span>Open Investigations</span>
                    <small>View saved research results</small>
                  </a>
                </li>
                <li>
                  <label className={styles.capabilityToggle}>
                    <span>
                      <strong>Search the web</strong>
                      <small>{capabilitySaving ? "Saving preference…" : webSearchEnabled ? "On for this conversation" : "Off for this conversation"}</small>
                    </span>
                    <input
                      aria-label="Web search"
                      checked={webSearchEnabled}
                      disabled={!profileVersionId || pending || conversationLoading || capabilitySaving}
                      onChange={(event) => { void setWebSearchCapability(event.target.checked); }}
                      type="checkbox"
                    />
                  </label>
                </li>
              </ul>
            </div> : null}
          </div>
          <span className={styles.assistantProfileBoundary}>Profile v{profileVersion ?? "—"} · {webSearchEnabled ? "web permitted" : "profile-only"}</span>
          <button className={styles.assistantSubmit} type="submit" disabled={!question.trim() || pending || conversationLoading || !profileVersionId} aria-label="Send question">
            <ArrowUp size={17} weight="bold" aria-hidden="true" />
          </button>
        </div>
      </form>
    </section>
  );
}
