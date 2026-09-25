import { FormEvent, useEffect, useId, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { ArrowUp, CaretDown, CaretLeft, Paperclip, Plus } from "@phosphor-icons/react";
import { createChatConversation, getChatConversation, sendChatMessageStream, updateChatCapabilities } from "../../api/chat";
import {
  attachBriefingContext,
  attachResearchContext,
  getManagedResearchJobs,
  getResearchContextAttachments,
  previewManagedResearchBrief,
  removeBriefingContext,
  removeResearchContext,
  startManagedResearch,
  type ManagedResearchBriefPreview,
  type ManagedResearchJob,
  type ResearchContextAttachment,
} from "../../api/managedResearch";
import { getBriefings, type BriefingListItem } from "../../api/briefings";
import type { ChatMessage } from "../../types/chat";
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
function researchInvestigationHref(companyId: string, job: ManagedResearchJob) {
  const id = job.investigationId ?? job.id;
  const params = new URLSearchParams({ tab: "investigations", research: id });
  if (job.answerInChat && job.conversationId) params.set("conversation", job.conversationId);
  return `/companies/${encodeURIComponent(companyId)}?${params.toString()}`;
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
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [question, setQuestion] = useState("");
  const [webSearchEnabled, setWebSearchEnabled] = useState(false);
  const [capabilitiesOpen, setCapabilitiesOpen] = useState(false);
  const [activeCapability, setActiveCapability] = useState<ComposerCapability | null>(null);
  const [deepResearchBrief, setDeepResearchBrief] = useState<ManagedResearchBriefPreview | null>(null);
  const [deepResearchOriginalQuestion, setDeepResearchOriginalQuestion] = useState<string | null>(null);
  const [deepResearchBriefEditing, setDeepResearchBriefEditing] = useState(false);
  const [deepResearchBriefLoading, setDeepResearchBriefLoading] = useState(false);
  const [deepResearchStarting, setDeepResearchStarting] = useState(false);
  const [conversationLoading, setConversationLoading] = useState(false);
  const [capabilitySaving, setCapabilitySaving] = useState(false);
  const [pending, setPending] = useState(false);
  const [expandedSourceMessageId, setExpandedSourceMessageId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [managedResearchJobs, setManagedResearchJobs] = useState<ManagedResearchJob[]>([]);
  const [briefings, setBriefings] = useState<BriefingListItem[]>([]);
  const [researchContextAttachments, setResearchContextAttachments] = useState<ResearchContextAttachment[]>([]);
  const [attachingResearchId, setAttachingResearchId] = useState<string | null>(null);
  const [investigationPickerOpen, setInvestigationPickerOpen] = useState(false);
  const [investigationSearch, setInvestigationSearch] = useState("");
  const capabilitiesId = useId();
  const capabilitiesRef = useRef<HTMLDivElement>(null);
  const capabilitiesTriggerRef = useRef<HTMLButtonElement>(null);
  const addEvidenceActionRef = useRef<HTMLButtonElement>(null);
  const questionInputRef = useRef<HTMLTextAreaElement>(null);
  const activeCompanyIdRef = useRef(companyId);
  const researchContextLoadVersionRef = useRef(0);
  const researchBriefVersionRef = useRef(0);
  const researched = formatDate(lastResearchedAt);

  const setConversationInUrl = (nextConversationId: string | null) => {
    const next = new URLSearchParams(searchParams);
    if (nextConversationId) next.set("conversation", nextConversationId);
    else next.delete("conversation");
    setSearchParams(next, { replace: true });
  };

  const startNewConversation = () => {
    if (pending || conversationLoading) return;
    researchBriefVersionRef.current += 1;
    setConversationId(null);
    setMessages([]);
    setQuestion("");
    setWebSearchEnabled(false);
    setActiveCapability(null);
    setDeepResearchBrief(null);
    setDeepResearchOriginalQuestion(null);
    setDeepResearchBriefEditing(false);
    setDeepResearchBriefLoading(false);
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
    const companyChanged = activeCompanyIdRef.current !== companyId;
    activeCompanyIdRef.current = companyId;
    if (!requestedConversationId) {
      // Investigation and dossier navigation changes other query parameters.
      // Keep the mounted company's active chat unless the user explicitly
      // starts a new one; otherwise opening a running Investigation erases the
      // visible history even though the durable conversation still exists.
      if (companyChanged || !conversationId) {
        setConversationId(null);
        setMessages([]);
        setWebSearchEnabled(false);
      }
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
    if (initialCapability !== "deepResearch" || !profileVersionId) return;
    setActiveCapability("deepResearch");
    setCapabilitiesOpen(false);
    if (initialQuestion) setQuestion(initialQuestion);
    window.setTimeout(() => questionInputRef.current?.focus(), 0);
  }, [initialCapability, initialQuestion, profileVersionId]);

  useEffect(() => {
    if (!conversationId || pending || !managedResearchJobs.some((job) => job.answerInChat && job.conversationId === conversationId)) return;
    let active = true;
    const refresh = async () => {
      try {
        const conversation = await getChatConversation(companyId, conversationId);
        if (!active || !conversation?.messages) return;
        setMessages(conversation.messages);
        const attachments = await getResearchContextAttachments(companyId, conversationId);
        if (active) setResearchContextAttachments(attachments);
      } catch { /* A later poll can recover the durable chat history. */ }
    };
    void refresh();
    const interval = window.setInterval(() => { void refresh(); }, 10_000);
    return () => { active = false; window.clearInterval(interval); };
  }, [companyId, conversationId, pending, managedResearchJobs]);

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
    addEvidenceActionRef.current?.focus();
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
    let active = true;
    void getBriefings(companyId)
      .then((items) => { if (active) setBriefings(Array.isArray(items) ? items : []); })
      .catch(() => { if (active) setBriefings([]); });
    return () => { active = false; };
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
          setResearchContextAttachments(attachments);
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

  const cancelDeepResearchBrief = () => {
    if (!deepResearchBrief || deepResearchStarting) return;
    researchBriefVersionRef.current += 1;
    setQuestion(deepResearchOriginalQuestion ?? "");
    setDeepResearchBrief(null);
    setDeepResearchOriginalQuestion(null);
    setDeepResearchBriefEditing(false);
    window.setTimeout(() => questionInputRef.current?.focus(), 0);
  };

  const startDeepResearchBrief = async () => {
    if (!profileVersionId || !deepResearchBrief || deepResearchStarting || pending) return;
    if (researchContextAttachments.length + managedResearchJobs.filter((job) => job.answerInChat && job.conversationId === conversationId && (job.status === "Queued" || job.status === "Researching")).length >= 5) {
      setError("Remove an attached Investigation before starting another Deep Research in this chat.");
      return;
    }
    const approvedQuestion = deepResearchBrief.question.trim();
    if (!approvedQuestion || approvedQuestion.length > 4_000) {
      setError("Enter one research question of at most 4,000 characters before starting Deep Research.");
      return;
    }

    setError(null);
    setDeepResearchStarting(true);
    try {
      // The brief remains local until it is explicitly approved. The server
      // validates the current company-context revision before queueing work.
      const activeConversationId = conversationId ?? (await beginConversation(false)).id;
      const job = await startManagedResearch(companyId, approvedQuestion, {
        conversationId: activeConversationId,
        contextRevision: deepResearchBrief.contextRevision,
        answerInChat: true,
      });
      const createdAt = new Date().toISOString();
      addManagedResearchJob(job);
      setMessages((current) => [...current,
        {
          id: `local-user-${Date.now()}`,
          role: "User",
          content: approvedQuestion,
          status: "Completed",
          citations: [],
          webEvidenceSnapshots: [],
          toolExecutions: [],
          createdAt,
        },
      ]);
      setConversationInUrl(activeConversationId);
      setQuestion("");
      setDeepResearchBrief(null);
      setDeepResearchOriginalQuestion(null);
      setDeepResearchBriefEditing(false);
      setActiveCapability(null);
      setCapabilitiesOpen(false);
    } catch (submissionError) {
      setError(submissionError instanceof Error ? submissionError.message : "Deep Research could not start.");
    } finally {
      setDeepResearchStarting(false);
    }
  };

  const ensureConversation = async () => {
    if (conversationId) return conversationId;
    const conversation = await beginConversation();
    return conversation.id;
  };

  const removeAttachment = async (attachment: ResearchContextAttachment) => {
    if (!conversationId) return;
    try {
      if (attachment.kind === "Briefing" && attachment.briefingId) {
        await removeBriefingContext(companyId, attachment.briefingId, conversationId);
      } else if (attachment.investigationId) {
        await removeResearchContext(companyId, attachment.investigationId, conversationId);
      } else return;
      researchContextLoadVersionRef.current += 1;
      setResearchContextAttachments((current) => current.filter((item) => item.id !== attachment.id));
    } catch (removalError) {
      setError(removalError instanceof Error ? removalError.message : "Could not remove the attached research context.");
    }
  };

  const attachBriefing = async (briefing: BriefingListItem) => {
    if (!profileVersionId || attachingResearchId) return;
    const existing = researchContextAttachments.find((item) => item.kind === "Briefing" && item.briefingId === briefing.id);
    if (!existing && researchContextAttachments.length >= 5) return;

    const operationId = `briefing:${briefing.id}`;
    setAttachingResearchId(operationId);
    setError(null);
    try {
      const activeConversationId = await ensureConversation();
      const attachment = await attachBriefingContext(companyId, briefing.id, briefing.versionNumber, activeConversationId);
      researchContextLoadVersionRef.current += 1;
      setResearchContextAttachments((current) => [
        attachment,
        ...current.filter((item) => item.id !== attachment.id && item.briefingId !== attachment.briefingId),
      ]);
      setInvestigationPickerOpen(false);
      setCapabilitiesOpen(false);
    } catch (attachmentError) {
      setError(attachmentError instanceof Error ? attachmentError.message : "Could not attach this Briefing.");
    } finally {
      setAttachingResearchId(null);
    }
  };

  const attachInvestigation = async (job: ManagedResearchJob) => {
    if (!profileVersionId || !job.investigationId || attachingResearchId || researchContextAttachments.length >= 5) return;

    setAttachingResearchId(job.id);
    setError(null);
    try {
      const activeConversationId = await ensureConversation();
      const attachment = await attachResearchContext(companyId, job.investigationId, activeConversationId);
      researchContextLoadVersionRef.current += 1;
      setResearchContextAttachments((current) => [
        attachment,
        ...current.filter((item) => item.id !== attachment.id && item.investigationId !== attachment.investigationId),
      ]);
      setInvestigationPickerOpen(false);
      setCapabilitiesOpen(false);
    } catch (attachmentError) {
      setError(attachmentError instanceof Error ? attachmentError.message : "Could not attach this investigation.");
    } finally {
      setAttachingResearchId(null);
    }
  };

  const submitQuestion = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const trimmed = question.trim();
    if (!trimmed || !profileVersionId || pending || deepResearchBriefLoading || deepResearchStarting || deepResearchBrief) return;

    if (activeCapability === "deepResearch") {
      setError(null);
      const previewVersion = ++researchBriefVersionRef.current;
      setDeepResearchBriefLoading(true);
      try {
        const preview = await previewManagedResearchBrief(companyId, trimmed);
        if (previewVersion !== researchBriefVersionRef.current) return;
        setDeepResearchBrief(preview);
        setDeepResearchOriginalQuestion(trimmed);
        setDeepResearchBriefEditing(false);
        setQuestion("");
      } catch (submissionError) {
        if (previewVersion !== researchBriefVersionRef.current) return;
        setError(submissionError instanceof Error ? submissionError.message : "The research question could not be prepared. Retry to continue.");
      } finally {
        if (previewVersion === researchBriefVersionRef.current) setDeepResearchBriefLoading(false);
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
        <span className={styles.handoffStatus}>{pending ? "Thinking" : deepResearchBriefLoading ? "Preparing research question" : deepResearchStarting ? "Launching research" : conversationLoading ? "Loading chat" : profileVersionId ? webSearchEnabled ? "Web permitted" : "Profile only" : "Profile required"}</span>
      </header>

      <div className={styles.chatViewport} aria-live="polite" aria-label="Ask RAVEN conversation">
        {messages.length === 0 && !deepResearchBrief && !deepResearchBriefLoading ? <div className={styles.chatEmptyState}>
          <span className={styles.chatEmptyMark} aria-hidden="true">✦</span>
          <strong>{profileVersionId ? "Ask about this company" : "Accept a profile first"}</strong>
          <p>{profileVersionId ? "Answers are grounded in the accepted profile and its evidence." : "Ask RAVEN and Deep Research in chat require an accepted company profile."}</p>
          {profileVersionId ? <div className={styles.starterList} aria-label="Suggested questions">
            {starterPrompts.map((prompt) => <button className={styles.starterPrompt} key={prompt} type="button" onClick={() => { setQuestion(prompt); questionInputRef.current?.focus(); }}>{prompt}</button>)}
          </div> : null}
        </div> : messages.map((message) => (
          <article key={message.id} className={`${styles.chatMessage} ${message.role === "User" ? styles.chatMessageUser : styles.chatMessageAssistant}`}>
            <span className={styles.chatMessageRole}>{message.role === "User" ? "You" : "RAVEN"}</span>
            {message.content ? <p>{message.content}</p> : null}
            {message.followUpQuestion ? <p className={styles.chatFollowUp}>{message.followUpQuestion}</p> : null}
            {message.citations.length > 0 ? <>
              <button className={styles.sourceToggle} type="button" aria-expanded={expandedSourceMessageId === message.id} onClick={() => setExpandedSourceMessageId((current) => current === message.id ? null : message.id)}>
                <span>Nguồn</span><CaretDown className={styles.sourceChevron} size={13} aria-hidden="true" />
              </button>
              {expandedSourceMessageId === message.id ? <section className={styles.sourceList} aria-label="Sources used for this answer">
                {message.citations.map((citation) => <a key={citation.briefingVersionId ?? citation.investigationId ?? citation.webEvidenceSnapshotId ?? citation.sourceDocumentId ?? citation.url} className={styles.sourceItem} href={citation.url} target={citation.origin === "Investigation" || citation.origin === "Briefing" ? undefined : "_blank"} rel={citation.origin === "Investigation" || citation.origin === "Briefing" ? undefined : "noreferrer"}>
                  <span>{citation.origin === "Investigation" ? "Investigation" : citation.origin === "Briefing" ? "Briefing" : citation.origin === "Web" ? "Web source" : "Profile source"}</span>
                  <strong>{citation.title ?? citation.fieldPath ?? sourceDomain(citation.url)}</strong>
                  {citation.origin === "Web" || citation.origin === "Profile" ? <small>{sourceDomain(citation.url)}</small> : null}
                </a>)}
              </section> : null}
            </> : null}
          </article>
        ))}
        {deepResearchBriefLoading ? <div className={styles.researchBriefPreparing} role="status">Preparing research question…</div> : null}
        {deepResearchBrief ? <section className={styles.researchBriefCard} data-testid="deep-research-brief" aria-labelledby="deep-research-brief-heading">
          <header className={styles.researchBriefHeader}>
            <h3 id="deep-research-brief-heading">Review question</h3>
            <button type="button" className="button button--quiet" aria-label={deepResearchBriefEditing ? "Done editing" : "Edit question"} onClick={() => setDeepResearchBriefEditing((editing) => !editing)} disabled={deepResearchStarting}>
              {deepResearchBriefEditing ? "Done" : "Edit"}
            </button>
          </header>
          <div className={styles.researchBriefContext} aria-label="Research context">
            <span className={styles.researchBriefCompany} title={companyName}><strong>Company:</strong> {companyName}</span>
            <span className={styles.researchBriefProfile}>{profileVersionId && profileVersion ? `v${profileVersion} · ${sourceCount} sources` : "Identity only"}</span>
          </div>
          <div className={styles.researchBriefRecord}>
            {deepResearchBriefEditing ? <label className={styles.researchBriefEditor}>Question
              <textarea aria-label="Research question" value={deepResearchBrief.question} onChange={(event) => setDeepResearchBrief((current) => current ? { ...current, question: event.target.value } : current)} rows={3} maxLength={4_000} />
            </label> : <p className={styles.researchBriefQuestion}>{deepResearchBrief.question}</p>}
          </div>
          <footer className={styles.researchBriefActions}>
            <button type="button" className="button button--quiet" onClick={cancelDeepResearchBrief} disabled={deepResearchStarting}>Cancel</button>
            <button type="button" className="button button--ai" onClick={() => void startDeepResearchBrief()} disabled={deepResearchStarting || deepResearchBriefEditing}>{deepResearchStarting ? "Starting…" : "Start Deep Research"}</button>
          </footer>
        </section> : null}
        {pending ? <div className={styles.chatSystemMessage} role="status">{[...messages].reverse().find((message) => message.role === "Assistant" && message.status === "Pending")?.activity ?? "RAVEN is preparing an answer…"}</div> : null}
        {deepResearchStarting ? <div className={styles.chatSystemMessage} role="status">Starting Deep Research in the background...</div> : null}
        {managedResearchJobs.some((job) => job.answerInChat && job.conversationId === conversationId && (job.status === "Queued" || job.status === "Researching")) ? <div className={styles.chatSystemMessage} role="status">Deep Research is running. RAVEN will answer from the Investigation when it is ready.</div> : null}
        {error ? <div className={styles.chatSystemMessage} role="alert">{error}</div> : null}
      </div>

      <form className={styles.assistantComposer} onSubmit={submitQuestion}>
        <label className="sr-only" htmlFor="ask-raven-question">{activeCapability === "deepResearch" ? `Research about ${companyName}` : `Ask about ${companyName}`}</label>
        <textarea
          id="ask-raven-question"
          ref={questionInputRef}
          value={question}
          disabled={!profileVersionId || pending || deepResearchBriefLoading || deepResearchStarting || conversationLoading || deepResearchBrief !== null}
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
          <span className={styles.researchContextLabel}>Research context</span>
          {researchContextAttachments.map((attachment) => <span className={styles.researchContextChip} key={attachment.id}>
            <span title={attachment.kind === "Briefing" ? attachment.title ?? undefined : attachment.objective ?? undefined}>✦ {attachment.kind === "Briefing" ? `Briefing · ${attachment.title} · v${attachment.briefingVersionNumber}` : `Investigation · ${attachment.objective}`}</span>
            <button type="button" onClick={() => void removeAttachment(attachment)} aria-label={`Remove ${attachment.title ?? attachment.objective ?? "research"} context`}>×</button>
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
              onClick={() => {
                if (capabilitiesOpen) setInvestigationPickerOpen(false);
                setCapabilitiesOpen((open) => !open);
              }}
            >
              <Plus size={17} weight="bold" aria-hidden="true" />
            </button>
            {capabilitiesOpen ? <div aria-label="Additional capabilities" className={styles.capabilityPanel} id={capabilitiesId} role="group">
              {investigationPickerOpen ? <>
                <div className={styles.evidencePickerHeader}>
                  <button aria-label="Back to additional capabilities" className={styles.evidenceBackButton} type="button" onClick={() => setInvestigationPickerOpen(false)}>
                    <CaretLeft size={14} aria-hidden="true" /> Back
                  </button>
                  <strong>Attach evidence</strong>
                  <span>Use saved Investigations and Briefings in this chat.</span>
                </div>
                <section className={styles.evidencePickerPanel} aria-label="Choose evidence">
                  <input aria-label="Find research context" placeholder="Find Investigations or Briefings" value={investigationSearch} onChange={(event) => setInvestigationSearch(event.target.value)} />
                  <strong>Investigations</strong>
                  <div className={styles.evidencePickerList}>
                    {managedResearchJobs.filter((job) => job.status === "Completed" && job.investigationId && job.objective.toLowerCase().includes(investigationSearch.toLowerCase())).length === 0 ? <p>No completed Deep Research found.</p> :
                      managedResearchJobs.filter((job) => job.status === "Completed" && job.investigationId && job.objective.toLowerCase().includes(investigationSearch.toLowerCase())).map((job) => <div className={styles.evidencePickerRow} key={job.id}>
                        <span title={job.objective}>{job.objective}</span>
                        <button type="button" onClick={() => void attachInvestigation(job)} disabled={attachingResearchId !== null || researchContextAttachments.length >= 5 || researchContextAttachments.some((item) => item.investigationId === job.investigationId)}>
                          {researchContextAttachments.some((item) => item.investigationId === job.investigationId) ? "Added" : attachingResearchId === job.id ? "Adding…" : "Add"}
                        </button>
                      </div>)}
                  </div>
                  <strong>Briefings</strong>
                  <div className={styles.evidencePickerList}>
                    {briefings.filter((briefing) => `${briefing.title} ${briefing.template}`.toLowerCase().includes(investigationSearch.toLowerCase())).length === 0 ? <p>No Briefings found.</p> :
                      briefings.filter((briefing) => `${briefing.title} ${briefing.template}`.toLowerCase().includes(investigationSearch.toLowerCase())).map((briefing) => {
                        const attached = researchContextAttachments.find((item) => item.kind === "Briefing" && item.briefingId === briefing.id);
                        const current = attached?.briefingVersionNumber === briefing.versionNumber;
                        return <div className={styles.evidencePickerRow} key={briefing.id}>
                          <span title={briefing.title}>{briefing.title} · v{briefing.versionNumber}</span>
                          <button type="button" onClick={() => void attachBriefing(briefing)} disabled={attachingResearchId !== null || (!attached && researchContextAttachments.length >= 5) || current}>
                            {current ? "Added" : attachingResearchId === `briefing:${briefing.id}` ? "Adding…" : attached ? `Update to v${briefing.versionNumber}` : "Add"}
                          </button>
                        </div>;
                      })}
                  </div>
                  {researchContextAttachments.length >= 5 ? <small>Remove one context item to add another.</small> : null}
                </section>
              </> : <>
                <div className={styles.capabilityPanelHeader}>
                  <strong>Additional capabilities</strong>
                  <span>Choose a next step.</span>
                </div>
                <ul className={styles.capabilityList}>
                  {profileVersionId ? <li>
                    <button
                      aria-label="Attach evidence"
                      className={styles.capabilityAction}
                      ref={addEvidenceActionRef}
                      type="button"
                      onClick={() => {
                        setInvestigationSearch("");
                        setInvestigationPickerOpen(true);
                      }}
                    >
                      <span className={styles.evidenceActionLabel}><Paperclip size={14} aria-hidden="true" /> Attach evidence{researchContextAttachments.length ? ` (${researchContextAttachments.length}/5)` : ""}</span>
                      <small>Use saved Investigations and Briefings in this chat</small>
                    </button>
                  </li> : null}
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
                      disabled={!profileVersionId}
                      onClick={() => {
                        setActiveCapability("deepResearch");
                        setDeepResearchBrief(null);
                        setDeepResearchOriginalQuestion(null);
                        setDeepResearchBriefEditing(false);
                        setCapabilitiesOpen(false);
                        window.setTimeout(() => questionInputRef.current?.focus(), 0);
                      }}
                    >
                      <span>Deep Research</span>
                      <small>{profileVersionId ? "Investigate, then answer in this chat" : "Accept a profile to use in chat"}</small>
                    </button>
                  </li>
                </ul>
              </>}
            </div> : null}
          </div>
          {activeCapability === "deepResearch" ? <button className={styles.capabilityChip} type="button" onClick={() => { researchBriefVersionRef.current += 1; setDeepResearchBriefLoading(false); setActiveCapability(null); setDeepResearchBrief(null); setDeepResearchOriginalQuestion(null); setDeepResearchBriefEditing(false); }} aria-label="Remove Deep Research capability">✦ Deep Research ×</button> : null}
          <span className={styles.assistantProfileBoundary} title={profileVersionId ? "Answers can use the accepted profile and attached research" : "An accepted profile is required for chat"}><span aria-hidden="true">◉</span> {profileVersionId ? `v${profileVersion} · ${webSearchEnabled ? "web permitted" : "profile-only"}` : "Profile required for Chat"}</span>
          <button className={styles.assistantSubmit} type="submit" disabled={!question.trim() || !profileVersionId || pending || deepResearchBriefLoading || deepResearchStarting || deepResearchBrief !== null || conversationLoading} aria-label={activeCapability === "deepResearch" ? "Review research question" : "Send question"}>
            <ArrowUp size={17} weight="bold" aria-hidden="true" />
          </button>
        </div>
      </form>
    </section>
  );
}
