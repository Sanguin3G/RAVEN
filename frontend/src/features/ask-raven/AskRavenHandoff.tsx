import { FormEvent, useEffect, useId, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { ArrowUp, CaretDown, Plus } from "@phosphor-icons/react";
import { createChatConversation, getChatConversation, sendChatMessageStream, updateChatCapabilities } from "../../api/chat";
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
import styles from "./ask-raven.module.css";

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
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedConversationId = searchParams.get("conversation");
  const [conversationId, setConversationId] = useState<string | null>(requestedConversationId);
  const [messages, setMessages] = useState<HandoffMessage[]>([]);
  const [question, setQuestion] = useState("");
  const [webSearchEnabled, setWebSearchEnabled] = useState(false);
  const [capabilitiesOpen, setCapabilitiesOpen] = useState(false);
  const [activeCapability, setActiveCapability] = useState<ComposerCapability | null>(null);
  const [deepResearchStarting, setDeepResearchStarting] = useState(false);
  const [conversationLoading, setConversationLoading] = useState(false);
  const [capabilitySaving, setCapabilitySaving] = useState(false);
  const [pending, setPending] = useState(false);
  const [expandedSourceMessageId, setExpandedSourceMessageId] = useState<string | null>(null);
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
    setActiveCapability(null);
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
    const conversation = await beginConversation();
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

    if (activeCapability === "deepResearch") {
      setError(null);
      const userMessage: HandoffMessage = {
        id: `local-user-${Date.now()}`,
        role: "User",
        content: trimmed,
        status: "Completed",
        citations: [],
        webEvidenceSnapshots: [],
        toolExecutions: [],
        createdAt: new Date().toISOString(),
      };
      setMessages((current) => [...current, userMessage]);
      setQuestion("");
      setDeepResearchStarting(true);
      setActiveCapability(null);
      setCapabilitiesOpen(false);

      try {
        // Creating the conversation here gives the durable managed job a
        // conversation relationship without fabricating a ChatMessage. The
        // existing Chat API has no launch-message-only operation.
        // A Deep Research launch is not a persisted ChatMessage. Keep a newly
        // created conversation off the URL until a normal Chat turn exists;
        // otherwise the restore effect can replace this local launch status
        // with an empty server-side message list.
        const activeConversationId = conversationId ?? (await beginConversation(false)).id;
        const job = await startManagedResearch(companyId, trimmed, { conversationId: activeConversationId });
        addManagedResearchJob(job);
        setMessages((current) => [...current, {
          id: `managed-research-started-${job.id}`,
          role: "Assistant",
          content: `Deep Research started\n\nRunning in the background. Results will appear in Investigations.`,
          status: "Completed",
          citations: [],
          webEvidenceSnapshots: [],
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
        <span className={styles.handoffStatus}>{pending ? "Thinking" : deepResearchStarting ? "Launching research" : conversationLoading ? "Loading chat" : profileVersionId ? webSearchEnabled ? "Web permitted" : "Profile only" : activeCapability === "deepResearch" ? "Deep Research ready" : "Profile required"}</span>
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
            {message.content ? <p>{message.content}</p> : null}
            {message.answerStatus ? <div className={styles.chatMessageMeta}><span className={styles.chatMessageStatus}>{statusLabels[message.answerStatus]}</span>{getAnswerEvidenceMode(message) ? <span className={`${styles.answerEvidenceBadge} ${styles[`answerEvidenceBadge${getAnswerEvidenceMode(message)}`]}`}>{getAnswerEvidenceMode(message)}</span> : null}</div> : null}
            {message.followUpQuestion ? <p className={styles.chatFollowUp}>{message.followUpQuestion}</p> : null}
            {message.managedResearchJobId ? <a className={styles.researchMessageLink} href={researchInvestigationHref(companyId, managedResearchJobs.find((job) => job.id === message.managedResearchJobId) ?? {
              id: message.managedResearchJobId,
              companyId,
              objective: message.content,
              status: "Queued",
              createdAt: message.createdAt,
            })}>View investigation</a> : null}
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
          disabled={(!profileVersionId && activeCapability !== "deepResearch") || pending || deepResearchStarting || conversationLoading}
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
          <span className={styles.assistantProfileBoundary} title={profileVersionId ? "Normal answers use the accepted Company Profile and its evidence" : "Deep Research can start from company identity and current research context"}><span aria-hidden="true">◉</span> {profileVersionId ? `v${profileVersion} · ${webSearchEnabled ? "web permitted" : "profile-only"}` : activeCapability === "deepResearch" ? "Company context · no accepted profile yet" : "Profile required for Chat"}</span>
          <button className={styles.assistantSubmit} type="submit" disabled={!question.trim() || pending || deepResearchStarting || conversationLoading || (activeCapability !== "deepResearch" && !profileVersionId)} aria-label={activeCapability === "deepResearch" ? "Start Deep Research" : "Send question"}>
            <ArrowUp size={17} weight="bold" aria-hidden="true" />
          </button>
        </div>
      </form>
    </section>
  );
}
