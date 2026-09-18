import { FormEvent, useEffect, useId, useRef, useState } from "react";
import { ArrowUp, Plus, Sparkle } from "@phosphor-icons/react";
import { createChatConversation, sendChatMessage } from "../../api/chat";
import { getManagedResearchJobs, startManagedResearch, type ManagedResearchJob, type ManagedResearchJobStatus } from "../../api/managedResearch";
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

const starterPrompts = [
  "What does this company do?",
  "Who are its key leaders?",
  "Where does it operate?",
  "What changed recently?",
  "Show me the supporting sources.",
];

type ComposerCapability = "webSearch" | "deepResearch";
type HandoffMessage = ChatMessage & {
  managedResearchJobId?: string;
};

function isRecentCompletion(job: ManagedResearchJob) {
  if (job.status !== "Completed" || !job.completedAt) return false;
  const completedAt = Date.parse(job.completedAt);
  return !Number.isNaN(completedAt) && Date.now() - completedAt <= 24 * 60 * 60 * 1000;
}

function researchInvestigationHref(companyId: string, job: ManagedResearchJob) {
  const id = job.investigationId ?? job.id;
  return `/companies/${encodeURIComponent(companyId)}?tab=investigations&research=${encodeURIComponent(id)}`;
}

export function AskRavenHandoff({ companyId, companyName, profileVersion, profileVersionId, sourceCount, lastResearchedAt }: AskRavenHandoffProps) {
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [messages, setMessages] = useState<HandoffMessage[]>([]);
  const [question, setQuestion] = useState("");
  const [capabilitiesOpen, setCapabilitiesOpen] = useState(false);
  const [activeCapability, setActiveCapability] = useState<ComposerCapability | null>(null);
  const [pending, setPending] = useState(false);
  const [deepResearchStarting, setDeepResearchStarting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [managedResearchJobs, setManagedResearchJobs] = useState<ManagedResearchJob[]>([]);
  const [completionNotification, setCompletionNotification] = useState<ManagedResearchJob | null>(null);
  const capabilitiesId = useId();
  const capabilitiesRef = useRef<HTMLDivElement>(null);
  const capabilitiesTriggerRef = useRef<HTMLButtonElement>(null);
  const investigationsLinkRef = useRef<HTMLAnchorElement>(null);
  const questionInputRef = useRef<HTMLTextAreaElement>(null);
  const notificationRef = useRef<HTMLDivElement>(null);
  const managedResearchStatusesRef = useRef<Record<string, ManagedResearchJobStatus>>({});
  const researched = formatDate(lastResearchedAt);

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

  useEffect(() => {
    let active = true;
    let initialized = false;

    const refreshManagedResearch = async () => {
      try {
        const jobs = await getManagedResearchJobs(companyId);
        if (!active) return;

        const previousStatuses = managedResearchStatusesRef.current;
        const newlyCompleted = jobs.find((job) => {
          if (job.status !== "Completed") return false;
          const previousStatus = previousStatuses[job.id];
          return previousStatus !== undefined
            ? previousStatus !== "Completed"
            : isRecentCompletion(job);
        });

        managedResearchStatusesRef.current = Object.fromEntries(jobs.map((job) => [job.id, job.status]));
        setManagedResearchJobs(jobs);
        initialized = true;

        if (newlyCompleted) {
          setCompletionNotification(newlyCompleted);
        }
      } catch {
        // Chat remains usable when the optional managed-research status endpoint
        // is unavailable. The durable job can be discovered on a later poll.
      }
    };

    void refreshManagedResearch();
    const intervalId = window.setInterval(() => { void refreshManagedResearch(); }, 10_000);
    return () => {
      active = false;
      window.clearInterval(intervalId);
    };
  }, [companyId]);

  useEffect(() => {
    const notification = notificationRef.current as (HTMLDivElement & { showPopover?: () => void; hidePopover?: () => void }) | null;
    if (!notification || !completionNotification) return;
    try {
      notification.showPopover?.();
    } catch {
      // Browsers without Popover support use the regular positioned fallback.
    }
    const timeoutId = window.setTimeout(() => {
      try {
        notification.hidePopover?.();
      } catch {
        // Ignore unsupported Popover APIs; React still removes the toast.
      }
      setCompletionNotification(null);
    }, 8_000);
    return () => window.clearTimeout(timeoutId);
  }, [completionNotification]);

  useEffect(() => {
    const input = questionInputRef.current;
    if (!input || !profileVersionId) return;
    input.placeholder = activeCapability === "deepResearch"
      ? `What would you like RAVEN to investigate about ${companyName}...`
      : `Ask about ${companyName}...`;
  }, [activeCapability, companyName, profileVersionId]);

  const dismissCompletionNotification = () => {
    const notification = notificationRef.current as (HTMLDivElement & { hidePopover?: () => void }) | null;
    try {
      notification?.hidePopover?.();
    } catch {
      // Ignore unsupported Popover APIs.
    }
    setCompletionNotification(null);
  };

  const addManagedResearchJob = (job: ManagedResearchJob) => {
    managedResearchStatusesRef.current[job.id] = job.status;
    setManagedResearchJobs((current) => [job, ...current.filter((item) => item.id !== job.id)]);
  };

  const submitQuestion = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const trimmed = question.trim();
    if (!trimmed || pending || deepResearchStarting || !profileVersionId) return;

    setError(null);
    const userMessage: HandoffMessage = {
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

    if (activeCapability === "deepResearch") {
      setDeepResearchStarting(true);
      setActiveCapability(null);
      setCapabilitiesOpen(false);

      try {
        const job = await startManagedResearch(companyId, trimmed);
        addManagedResearchJob(job);
        setMessages((current) => [...current, {
          id: `managed-research-started-${job.id}`,
          role: "Assistant",
          content: `Deep Research started\n\nInvestigating: "${trimmed}"\n\nYou can keep using RAVEN while this runs.`,
          status: "Completed",
          citations: [],
          toolExecutions: [],
          createdAt: new Date().toISOString(),
          managedResearchJobId: job.id,
        }]);
      } catch (submissionError) {
        setError(submissionError instanceof Error ? submissionError.message : "Deep Research could not start.");
      } finally {
        setDeepResearchStarting(false);
      }
      return;
    }

    setPending(true);

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
        <span className={styles.handoffStatus}>{pending ? "Thinking" : deepResearchStarting ? "Launching research" : profileVersionId ? "Profile only" : "Profile required"}</span>
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
            <p>{message.content}</p>
            {message.answerStatus ? <span className={styles.chatMessageStatus}>{statusLabels[message.answerStatus]}</span> : null}
            {message.followUpQuestion ? <p className={styles.chatFollowUp}>{message.followUpQuestion}</p> : null}
            {message.managedResearchJobId ? <a className={styles.researchMessageLink} href={researchInvestigationHref(companyId, managedResearchJobs.find((job) => job.id === message.managedResearchJobId) ?? {
              id: message.managedResearchJobId,
              companyId,
              objective: message.content,
              status: "Queued",
              createdAt: message.createdAt,
            })}>View investigation</a> : null}
            {message.citations.length > 0 ? <div className={styles.chatCitations}>
              {message.citations.map((citation) => <a key={citation.sourceDocumentId} className={styles.chatCitation} href={citation.url} target="_blank" rel="noreferrer">{citation.title ?? citation.fieldPath ?? "Profile source"}</a>)}
            </div> : null}
          </article>
        ))}
        {pending ? <div className={styles.chatSystemMessage} role="status">RAVEN is checking the accepted profile…</div> : null}
        {deepResearchStarting ? <div className={styles.chatSystemMessage} role="status">Starting Deep Research in the background...</div> : null}
        {error ? <div className={styles.chatSystemMessage} role="alert">{error}</div> : null}
      </div>

      <form className={styles.assistantComposer} onSubmit={submitQuestion}>
        {activeCapability === "deepResearch" ? <div className={styles.composerMode} role="status">
          <Sparkle size={15} weight="fill" aria-hidden="true" />
          <strong>Deep Research</strong>
          <span>Launches a background investigation; you can keep chatting.</span>
        </div> : null}
        <label className="sr-only" htmlFor="ask-raven-question">{activeCapability === "deepResearch" ? `Research about ${companyName}` : `Ask about ${companyName}`}</label>
        <textarea
          id="ask-raven-question"
          ref={questionInputRef}
          value={question}
          disabled={!profileVersionId || pending || deepResearchStarting}
          onChange={(event) => setQuestion(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) {
              event.preventDefault();
              event.currentTarget.form?.requestSubmit();
            }
          }}
          placeholder={profileVersionId
            ? activeCapability === "deepResearch"
              ? `What would you like RAVEN to investigate about ${companyName}?`
              : `Ask about ${companyName}…`
            : "Accept a profile to ask questions…"}
          rows={2}
        />
        <div className={styles.assistantComposerFooter}>
          {activeCapability === "deepResearch" ? <button className={styles.capabilityChip} type="button" onClick={() => setActiveCapability(null)} aria-label="Remove Deep Research capability">✦ Deep Research ×</button> : null}
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
                  <button className={`${styles.capabilityAction} ${styles.capabilityUnavailable}`} disabled type="button">
                    <span>Search the web</span>
                    <small>Coming in Day 8 — not available yet</small>
                  </button>
                </li>
                <li>
                  <button
                    aria-pressed={activeCapability === "deepResearch"}
                    className={`${styles.capabilityAction} ${activeCapability === "deepResearch" ? styles.capabilitySelected : ""}`}
                    type="button"
                    onClick={() => {
                      setActiveCapability("deepResearch");
                      setCapabilitiesOpen(false);
                      window.setTimeout(() => questionInputRef.current?.focus(), 0);
                    }}
                  >
                    <span>Deep Research</span>
                    <small>Investigate in the background and save the result</small>
                  </button>
                </li>
              </ul>
            </div> : null}
          </div>
          <span className={styles.assistantProfileBoundary}>Profile v{profileVersion ?? "—"} · accepted profile context</span>
          <button className={styles.assistantSubmit} type="submit" disabled={!question.trim() || pending || deepResearchStarting || !profileVersionId} aria-label={activeCapability === "deepResearch" ? "Start Deep Research" : "Send question"}>
            <ArrowUp size={17} weight="bold" aria-hidden="true" />
          </button>
        </div>
      </form>
      {completionNotification ? <div
        aria-live="polite"
        className={styles.researchToast}
        data-testid="managed-research-completion"
        popover="manual"
        ref={notificationRef}
        role="status"
      >
        <div>
          <strong>Deep Research finished</strong>
          <span>{companyName}</span>
        </div>
        <a href={researchInvestigationHref(companyId, completionNotification)} onClick={dismissCompletionNotification}>View result</a>
        <button type="button" onClick={dismissCompletionNotification} aria-label="Dismiss research notification">×</button>
      </div> : null}
    </section>
  );
}
