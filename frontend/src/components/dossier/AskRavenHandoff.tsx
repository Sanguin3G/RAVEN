import { FormEvent, useEffect, useId, useRef, useState } from "react";
import { ArrowUp, Plus } from "@phosphor-icons/react";
import { createChatConversation, sendChatMessage } from "../../api/chat";
import {
  attachResearchContext,
  getManagedResearchJobs,
  getResearchContextAttachments,
  removeResearchContext,
  startManagedResearch,
  type ManagedResearchJob,
  type ResearchContextAttachment,
} from "../../api/managedResearch";
import type { ChatAnswerStatus, ChatMessage } from "../../types/chat";
import { hasResearchActivity, upsertResearchActivity } from "../../utils/researchActivity";
import styles from "./AskRaven.module.css";

export interface AskRavenHandoffProps {
  companyId: string;
  companyName: string;
  profileVersion?: number | null;
  profileVersionId?: string | null;
  sourceCount: number;
  lastResearchedAt?: string | null;
  initialCapability?: "deepResearch";
  initialQuestion?: string | null;
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

function researchInvestigationHref(companyId: string, job: ManagedResearchJob) {
  const id = job.investigationId ?? job.id;
  return `/companies/${encodeURIComponent(companyId)}?tab=investigations&research=${encodeURIComponent(id)}`;
}

function syncManagedResearchActivity(companyName: string, job: ManagedResearchJob) {
  const activityId = `deep-${job.id}`;
  if ((job.status === "Completed" || job.status === "Failed" || job.status === "Cancelled") && !hasResearchActivity(activityId)) return;
  const terminalStatus = job.status === "Completed" ? "ready" : job.status === "Failed" || job.status === "Cancelled" ? "failed" : "running";
  const detail = job.status === "Queued"
    ? "Starting investigation"
    : job.status === "Researching"
      ? "Researching across sources"
      : job.status === "Completed"
        ? "Ready for review"
        : job.status === "Cancelled"
          ? "Research cancelled"
          : "Deep Research could not complete";
  upsertResearchActivity({
    id: activityId,
    jobId: job.id,
    origin: "Deep",
    companyId: job.companyId,
    companyName,
    objective: job.objective,
    detail,
    status: terminalStatus,
    href: researchInvestigationHref(job.companyId, job),
    updatedAt: job.completedAt || job.createdAt,
  });
}

export function AskRavenHandoff({ companyId, companyName, profileVersion, profileVersionId, sourceCount, lastResearchedAt, initialCapability, initialQuestion }: AskRavenHandoffProps) {
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [messages, setMessages] = useState<HandoffMessage[]>([]);
  const [question, setQuestion] = useState("");
  const [capabilitiesOpen, setCapabilitiesOpen] = useState(false);
  const [activeCapability, setActiveCapability] = useState<ComposerCapability | null>(null);
  const [pending, setPending] = useState(false);
  const [deepResearchStarting, setDeepResearchStarting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [managedResearchJobs, setManagedResearchJobs] = useState<ManagedResearchJob[]>([]);
  const [researchContextAttachments, setResearchContextAttachments] = useState<ResearchContextAttachment[]>([]);
  const [attachingResearchId, setAttachingResearchId] = useState<string | null>(null);
  const capabilitiesId = useId();
  const capabilitiesRef = useRef<HTMLDivElement>(null);
  const capabilitiesTriggerRef = useRef<HTMLButtonElement>(null);
  const investigationsLinkRef = useRef<HTMLAnchorElement>(null);
  const questionInputRef = useRef<HTMLTextAreaElement>(null);
  const researchContextLoadVersionRef = useRef(0);
  const researched = formatDate(lastResearchedAt);

  useEffect(() => {
    if (initialCapability !== "deepResearch") return;
    setActiveCapability("deepResearch");
    setCapabilitiesOpen(false);
    if (initialQuestion) setQuestion(initialQuestion);
    window.setTimeout(() => questionInputRef.current?.focus(), 0);
  }, [initialCapability, initialQuestion]);

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
    const refreshManagedResearch = async () => {
      try {
        const result = await getManagedResearchJobs(companyId);
        const jobs = Array.isArray(result) ? result : [];
        if (!active) return;

        setManagedResearchJobs(jobs);
        jobs.forEach((job) => syncManagedResearchActivity(companyName, job));
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
    if (!conversationId) {
      researchContextLoadVersionRef.current += 1;
      setResearchContextAttachments([]);
      return;
    }

    let active = true;
    const loadVersion = ++researchContextLoadVersionRef.current;
    void getResearchContextAttachments(companyId, conversationId)
      .then((attachments) => {
        if (active && loadVersion === researchContextLoadVersionRef.current) {
          setResearchContextAttachments((current) => [
            ...attachments,
            ...current.filter((item) => !attachments.some((loaded) => loaded.id === item.id)),
          ]);
        }
      })
      .catch(() => {
        // The Chat conversation remains usable when the optional context
        // endpoint is unavailable. Existing attachments remain local until a
        // later conversation reload.
      });
    return () => { active = false; };
  }, [companyId, conversationId]);

  useEffect(() => {
    const input = questionInputRef.current;
    if (!input) return;
    input.placeholder = activeCapability === "deepResearch"
      ? `What would you like RAVEN to investigate about ${companyName}...`
      : profileVersionId ? `Ask about ${companyName}…` : "Accept a profile to ask questions…";
  }, [activeCapability, companyName, profileVersionId]);

  const addManagedResearchJob = (job: ManagedResearchJob) => {
    setManagedResearchJobs((current) => [job, ...current.filter((item) => item.id !== job.id)]);
    syncManagedResearchActivity(companyName, job);
  };

  const ensureConversation = async () => {
    if (conversationId) return conversationId;
    const conversation = await createChatConversation(companyId);
    setConversationId(conversation.id);
    return conversation.id;
  };

  const removeAttachment = async (attachment: ResearchContextAttachment) => {
    if (!conversationId) return;
    try {
      await removeResearchContext(companyId, attachment.investigationId, conversationId);
      researchContextLoadVersionRef.current += 1;
      setResearchContextAttachments((current) => current.filter((item) => item.id !== attachment.id));
    } catch (removalError) {
      setError(removalError instanceof Error ? removalError.message : "Could not remove the attached investigation.");
    }
  };

  const attachInvestigation = async (job: ManagedResearchJob) => {
    if (!job.investigationId || attachingResearchId) return;

    setAttachingResearchId(job.id);
    setError(null);
    try {
      const activeConversationId = await ensureConversation();
      const attachment = await attachResearchContext(companyId, job.investigationId, activeConversationId);
      setResearchContextAttachments((current) => [
        attachment,
        ...current.filter((item) => item.id !== attachment.id && item.investigationId !== attachment.investigationId),
      ]);
    } catch (attachmentError) {
      setError(attachmentError instanceof Error ? attachmentError.message : "Could not attach this investigation.");
    } finally {
      setAttachingResearchId(null);
    }
  };

  const submitQuestion = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const trimmed = question.trim();
    if (!trimmed || pending || deepResearchStarting || (activeCapability !== "deepResearch" && !profileVersionId)) return;

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
        // Creating the conversation here gives the durable managed job a
        // conversation relationship without fabricating a ChatMessage. The
        // existing Chat API has no launch-message-only operation.
        const activeConversationId = await ensureConversation();
        const job = await startManagedResearch(companyId, trimmed, { conversationId: activeConversationId });
        addManagedResearchJob(job);
        setMessages((current) => [...current, {
          id: `managed-research-started-${job.id}`,
          role: "Assistant",
          content: `Deep Research started\n\nRunning in the background. Results will appear in Investigations.`,
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
      const activeConversationId = await ensureConversation();
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
        <span className={styles.handoffStatus}>{pending ? "Thinking" : deepResearchStarting ? "Launching research" : profileVersionId ? "Profile only" : activeCapability === "deepResearch" ? "Deep Research ready" : "Profile required"}</span>
      </header>

      <div className={styles.chatViewport} aria-live="polite" aria-label="Ask RAVEN conversation">
        {messages.length === 0 ? <div className={styles.chatEmptyState}>
          <span className={styles.chatEmptyMark} aria-hidden="true">✦</span>
          <strong>{profileVersionId ? "Ask about this company" : activeCapability === "deepResearch" ? "Start a Deep Research investigation" : "Accept a profile first"}</strong>
          <p>{profileVersionId ? "Answers are grounded in the accepted profile and its evidence." : activeCapability === "deepResearch" ? "Deep Research can investigate the company while the accepted profile is still incomplete." : "Ask RAVEN becomes available after a company profile is accepted."}</p>
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

      {managedResearchJobs.some((job) => job.status === "Completed" && job.investigationId) ? <div className={styles.completedResearchActions} aria-label="Completed research">
        <span className={styles.completedResearchLabel}>Ready research</span>
        {managedResearchJobs.filter((job) => job.status === "Completed" && job.investigationId).map((job) => <div className={styles.completedResearchAction} key={job.id}>
          <span title={job.objective}>Deep Research · {job.objective}</span>
          <a href={researchInvestigationHref(companyId, job)}>Open</a>
          <button type="button" onClick={() => void attachInvestigation(job)} disabled={attachingResearchId !== null}>
            {attachingResearchId === job.id ? "Attaching…" : "Attach to Ask RAVEN"}
          </button>
        </div>)}
      </div> : null}

      <form className={styles.assistantComposer} onSubmit={submitQuestion}>
        <label className="sr-only" htmlFor="ask-raven-question">{activeCapability === "deepResearch" ? `Research about ${companyName}` : `Ask about ${companyName}`}</label>
        <textarea
          id="ask-raven-question"
          ref={questionInputRef}
          value={question}
          disabled={(!profileVersionId && activeCapability !== "deepResearch") || pending || deepResearchStarting}
          onChange={(event) => setQuestion(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) {
              event.preventDefault();
              event.currentTarget.form?.requestSubmit();
            }
          }}
          placeholder={activeCapability === "deepResearch"
            ? `What would you like RAVEN to investigate about ${companyName}?`
            : profileVersionId ? `Ask about ${companyName}…` : "Accept a profile to ask questions…"}
          rows={2}
        />
        {researchContextAttachments.length > 0 ? <div className={styles.researchContextAttachments} aria-label="Attached research context" role="group">
          <span className={styles.researchContextLabel}>Attached context</span>
          {researchContextAttachments.map((attachment) => <span className={styles.researchContextChip} key={attachment.id}>
            <span title={attachment.objective}>✦ {attachment.objective}</span>
            <button type="button" onClick={() => void removeAttachment(attachment)} aria-label={`Remove ${attachment.objective} context`}>×</button>
          </span>)}
        </div> : null}
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
          {activeCapability === "deepResearch" ? <button className={styles.capabilityChip} type="button" onClick={() => setActiveCapability(null)} aria-label="Remove Deep Research capability">✦ Deep Research ×</button> : null}
          <span className={styles.assistantProfileBoundary} title={profileVersionId ? "Normal answers use the accepted Company Profile and its evidence" : "Deep Research can start from company identity and current research context"}><span aria-hidden="true">◉</span> {profileVersionId ? `v${profileVersion} · accepted profile` : activeCapability === "deepResearch" ? "Company context · no accepted profile yet" : "Profile required for Chat"}</span>
          <button className={styles.assistantSubmit} type="submit" disabled={!question.trim() || pending || deepResearchStarting || (activeCapability !== "deepResearch" && !profileVersionId)} aria-label={activeCapability === "deepResearch" ? "Start Deep Research" : "Send question"}>
            <ArrowUp size={17} weight="bold" aria-hidden="true" />
          </button>
        </div>
      </form>
    </section>
  );
}
