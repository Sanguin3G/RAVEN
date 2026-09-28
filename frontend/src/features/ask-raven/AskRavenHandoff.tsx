import { FormEvent, useCallback, useEffect, useId, useRef, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { ArrowDown, CaretDown } from "@phosphor-icons/react";
import { createChatConversation, deleteChatConversation, getChatConversation, listChatConversations, sendChatMessageStream, updateChatCapabilities } from "../../api/chat";
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
import { getInvestigations, type Investigation } from "../../api/investigations";
import type { ChatConversationResponse, ChatConversationSummary, ChatMessage } from "../../types/chat";
import { hasResearchActivity, upsertResearchActivity } from "../../utils/researchActivity";
import { chatDraftKey, getActiveChat, setActiveChat } from "./chatBrowserState";
import { AskRavenHeader } from "./AskRavenHeader";
import { AskRavenCapabilityMenu } from "./AskRavenCapabilityMenu";
import { AskRavenNextActionReview, type AskRavenNextActionKind } from "./AskRavenNextActionReview";
import { AskRavenComposer, type ComposerCapability } from "./AskRavenComposer";
import { useSpeechInput } from "./useSpeechInput";
import { AskRavenMessageActions, AskRavenUserMessageActions } from "./AskRavenMessageActions";
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

function sourceDomain(url: string) {
  try { return new URL(url).hostname; } catch { return url; }
}

function isIntentionalAbort(error: unknown, signal: AbortSignal) {
  return signal.aborted || (error instanceof DOMException && error.name === "AbortError");
}
const starterPrompts = [
  "What does this company do?",
  "Who are its key leaders?",
  "Where does it operate?",
  "What changed recently?",
  "Show me the supporting sources.",
];

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
  const [conversationProfileVersionId, setConversationProfileVersionId] = useState<string | null>(null);
  const [recentChats, setRecentChats] = useState<ChatConversationSummary[]>([]);
  const [recentChatsOpen, setRecentChatsOpen] = useState(false);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [question, setQuestion] = useState(() => {
    try { return localStorage.getItem(chatDraftKey(companyId, requestedConversationId)) ?? ""; } catch { return ""; }
  });
  const speech = useSpeechInput(question, setQuestion);
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
  const [investigations, setInvestigations] = useState<Investigation[]>([]);
  const [briefings, setBriefings] = useState<BriefingListItem[]>([]);
  const [researchContextAttachments, setResearchContextAttachments] = useState<ResearchContextAttachment[]>([]);
  const [attachingResearchId, setAttachingResearchId] = useState<string | null>(null);
  const [investigationPickerOpen, setInvestigationPickerOpen] = useState(false);
  const [contextDetailsOpen, setContextDetailsOpen] = useState(false);
  const [investigationSearch, setInvestigationSearch] = useState("");
  const [nextActionReview, setNextActionReview] = useState<{ kind: AskRavenNextActionKind; messageId: string; value: string } | null>(null);
  const [nextActionBusy, setNextActionBusy] = useState(false);
  const [editingMessageId, setEditingMessageId] = useState<string | null>(null);
  const [editingMessageText, setEditingMessageText] = useState("");
  const [showLatest, setShowLatest] = useState(false);
  const capabilitiesId = useId();
  const capabilitiesRef = useRef<HTMLDivElement>(null);
  const capabilitiesTriggerRef = useRef<HTMLButtonElement>(null);
  const addResearchContextActionRef = useRef<HTMLButtonElement>(null);
  const questionInputRef = useRef<HTMLTextAreaElement>(null);
  const chatViewportRef = useRef<HTMLDivElement>(null);
  const activeCompanyIdRef = useRef(companyId);
  const hydratedConversationRef = useRef<string | null>(null);
  const skipFallbackRef = useRef(false);
  const draftScopeRef = useRef(chatDraftKey(companyId, requestedConversationId));
  const skipDraftWriteRef = useRef(false);
  const researchContextLoadVersionRef = useRef(0);
  const localAttachmentConversationRef = useRef<string | null>(null);
  const researchBriefVersionRef = useRef(0);
  const activeChatAbortRef = useRef<AbortController | null>(null);

  useEffect(() => () => activeChatAbortRef.current?.abort(), []);

  const updateLatestVisibility = () => {
    const viewport = chatViewportRef.current;
    if (!viewport) return;
    setShowLatest(viewport.scrollHeight - viewport.scrollTop - viewport.clientHeight > 32);
  };

  useEffect(() => {
    const frame = window.requestAnimationFrame(updateLatestVisibility);
    return () => window.cancelAnimationFrame(frame);
  }, [messages, conversationId, pending]);

  const refreshResearchCatalog = useCallback(async () => {
    const [jobsResult, investigationsResult, briefingsResult] = await Promise.allSettled([
      getManagedResearchJobs(companyId),
      getInvestigations(companyId),
      getBriefings(companyId),
    ]);
    if (activeCompanyIdRef.current !== companyId) return;
    const jobs = jobsResult.status === "fulfilled" && Array.isArray(jobsResult.value) ? jobsResult.value : [];
    const currentInvestigations = investigationsResult.status === "fulfilled" && Array.isArray(investigationsResult.value) ? investigationsResult.value : [];
    const currentBriefings = briefingsResult.status === "fulfilled" && Array.isArray(briefingsResult.value) ? briefingsResult.value : [];
    setManagedResearchJobs(jobs);
    setInvestigations(currentInvestigations);
    setBriefings(currentBriefings);
    jobs.forEach((job) => syncManagedResearchActivity(companyName, job));
  }, [companyId, companyName]);

  const setConversationInUrl = (nextConversationId: string | null) => {
    const next = new URLSearchParams(searchParams);
    if (nextConversationId) next.set("conversation", nextConversationId);
    else next.delete("conversation");
    setSearchParams(next, { replace: true });
  };

  const startNewConversation = () => {
    if (pending || conversationLoading) return;
    skipFallbackRef.current = true;
    setActiveChat(companyId, null);
    try { localStorage.removeItem(chatDraftKey(companyId, null)); } catch { /* Optional draft storage. */ }
    researchBriefVersionRef.current += 1;
    setConversationId(null);
    hydratedConversationRef.current = null;
    localAttachmentConversationRef.current = null;
    setConversationProfileVersionId(null);
    setMessages([]);
    setQuestion("");
    setWebSearchEnabled(false);
    setActiveCapability(null);
    setDeepResearchBrief(null);
    setDeepResearchOriginalQuestion(null);
    setDeepResearchBriefEditing(false);
    setDeepResearchBriefLoading(false);
    setCapabilitiesOpen(false);
    setRecentChatsOpen(false);
    setExpandedSourceMessageId(null);
    setError(null);
    setConversationInUrl(null);
    questionInputRef.current?.focus();
  };

  const beginConversation = async (syncUrl = true) => {
    const created = await createChatConversation(companyId);
    setConversationId(created.id);
    hydratedConversationRef.current = created.id;
    setConversationProfileVersionId(created.profileVersionId);
    setActiveChat(companyId, created.id);
    setRecentChats((current) => [{ ...created, messageCount: 0 }, ...current].slice(0, 20));
    setWebSearchEnabled(created.webSearchEnabled ?? false);
    if (syncUrl) setConversationInUrl(created.id);
    return created;
  };

  const selectRecentChat = (id: string) => {
    if (pending || conversationLoading) return;
    setRecentChatsOpen(false);
    skipFallbackRef.current = false;
    if (id === conversationId) return;
    localAttachmentConversationRef.current = null;
    setConversationInUrl(id);
  };

  const deleteConversation = async (id: string) => {
    const chat = recentChats.find((item) => item.id === id);
    const title = chat?.title || "New chat";
    if (!window.confirm(`Delete “${title}”? This removes the conversation and its messages.`)) return;
    try {
      await deleteChatConversation(companyId, id);
      const remaining = recentChats.filter((item) => item.id !== id);
      setRecentChats(remaining);
      try { localStorage.removeItem(chatDraftKey(companyId, id)); } catch { /* Optional draft storage. */ }
      if (getActiveChat(companyId) === id) setActiveChat(companyId, null);
      if (conversationId === id || requestedConversationId === id) {
        skipFallbackRef.current = false;
        hydratedConversationRef.current = null;
        localAttachmentConversationRef.current = null;
        setConversationId(null);
        setMessages([]);
        setResearchContextAttachments([]);
        setConversationProfileVersionId(null);
        setWebSearchEnabled(false);
        setConversationInUrl(remaining[0]?.id ?? null);
      }
    } catch (deletionError) {
      setError(deletionError instanceof Error ? deletionError.message : "Could not delete this chat.");
    }
  };

  useEffect(() => {
    let active = true;
    const companyChanged = activeCompanyIdRef.current !== companyId;
    activeCompanyIdRef.current = companyId;
    if (!companyChanged && requestedConversationId && hydratedConversationRef.current === requestedConversationId) return () => { active = false; };
    if (!companyChanged && !requestedConversationId && (conversationId || skipFallbackRef.current)) return () => { active = false; };
    if (companyChanged) {
      activeChatAbortRef.current?.abort();
      hydratedConversationRef.current = null;
      localAttachmentConversationRef.current = null;
      skipFallbackRef.current = false;
      setConversationId(null);
      setMessages([]);
      setWebSearchEnabled(false);
      setResearchContextAttachments([]);
    }
    setConversationLoading(true);
    setError(null);
    const restore = async () => {
      let summaries: ChatConversationSummary[] = [];
      try {
        const response = await listChatConversations(companyId);
        summaries = Array.isArray(response) ? response : [];
        if (active) setRecentChats(summaries);
      } catch { /* An explicit or remembered conversation can still be restored. */ }
      const rememberedId = getActiveChat(companyId);
      const candidates = [requestedConversationId, rememberedId, ...summaries.map((item) => item.id)]
        .filter((id, index, ids): id is string => !!id && ids.indexOf(id) === index);
      for (const id of candidates) {
        try {
          const conversation: ChatConversationResponse = await getChatConversation(companyId, id);
          if (!active) return;
          setConversationId(conversation.id);
          hydratedConversationRef.current = conversation.id;
          localAttachmentConversationRef.current = null;
          setConversationProfileVersionId(conversation.profileVersionId);
          setMessages(conversation.messages);
          setWebSearchEnabled(conversation.webSearchEnabled);
          setActiveChat(companyId, conversation.id);
          if (requestedConversationId && requestedConversationId !== conversation.id) setConversationInUrl(conversation.id);
          return;
        } catch {
          if (id === rememberedId) setActiveChat(companyId, null);
        }
      }
      if (!active) return;
      setConversationId(null);
      hydratedConversationRef.current = null;
      setConversationProfileVersionId(null);
      setMessages([]);
      setWebSearchEnabled(false);
      if (requestedConversationId) setConversationInUrl(null);
    };
    void restore().finally(() => { if (active) setConversationLoading(false); });
    return () => { active = false; };
  }, [companyId, requestedConversationId]);

  useEffect(() => {
    const scope = chatDraftKey(companyId, conversationId);
    if (draftScopeRef.current === scope) return;
    draftScopeRef.current = scope;
    skipDraftWriteRef.current = true;
    try { setQuestion(localStorage.getItem(scope) ?? ""); } catch { setQuestion(""); }
  }, [companyId, conversationId]);

  useEffect(() => {
    if (skipDraftWriteRef.current) { skipDraftWriteRef.current = false; return; }
    try {
      const key = chatDraftKey(companyId, conversationId);
      if (question) localStorage.setItem(key, question);
      else localStorage.removeItem(key);
    } catch { /* Drafts are optional browser convenience state. */ }
  }, [companyId, conversationId, question]);

  const setWebSearchCapability = async (nextEnabled: boolean): Promise<string | null> => {
    if (!profileVersionId || pending || conversationLoading || capabilitySaving) return null;

    setCapabilitySaving(true);
    setError(null);
    try {
      const activeConversation = conversationId ? { id: conversationId } : await beginConversation();
      const updated = await updateChatCapabilities(companyId, activeConversation.id, { webSearchEnabled: nextEnabled });
      setConversationId(updated.id);
      setMessages(updated.messages);
      setWebSearchEnabled(updated.webSearchEnabled);
      setConversationInUrl(updated.id);
      return updated.id;
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Could not update Web Search for this conversation.");
      return null;
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
    addResearchContextActionRef.current?.focus();
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

    const intervalId = window.setInterval(() => { void refreshManagedResearch(); }, 10_000);
    return () => {
      active = false;
      window.clearInterval(intervalId);
    };
  }, [companyId]);

  useEffect(() => {
    void refreshResearchCatalog();
  }, [refreshResearchCatalog]);

  useEffect(() => {
    if (investigationPickerOpen) void refreshResearchCatalog();
  }, [investigationPickerOpen, refreshResearchCatalog]);

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
        if (active && loadVersion === researchContextLoadVersionRef.current && localAttachmentConversationRef.current !== conversationId) {
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
      try { localStorage.removeItem(chatDraftKey(companyId, conversationId)); } catch { /* Optional draft storage. */ }
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
      localAttachmentConversationRef.current = conversationId;
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
      localAttachmentConversationRef.current = activeConversationId;
      setResearchContextAttachments((current) => [
        attachment,
        ...current.filter((item) => item.id !== attachment.id && item.briefingId !== attachment.briefingId),
      ]);
    } catch (attachmentError) {
      setError(attachmentError instanceof Error ? attachmentError.message : "Could not attach this Briefing.");
    } finally {
      setAttachingResearchId(null);
    }
  };

  const attachInvestigation = async (investigation: Investigation) => {
    if (!profileVersionId || attachingResearchId || researchContextAttachments.length >= 5 || !investigation.materialId) return;

    setAttachingResearchId(investigation.id);
    setError(null);
    try {
      const activeConversationId = await ensureConversation();
      const attachment = await attachResearchContext(companyId, investigation.materialId, activeConversationId);
      researchContextLoadVersionRef.current += 1;
      localAttachmentConversationRef.current = activeConversationId;
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

  const sendNormalQuestion = async (trimmed: string, preferredConversationId: string | null, clearComposer: boolean) => {
    if (!trimmed || !profileVersionId || pending || conversationLoading) return;

    const localUserId = `local-user-${Date.now()}`;
    const localAssistantId = `local-assistant-${Date.now()}`;
    const draftAtSend = chatDraftKey(companyId, conversationId);
    setPending(true);
    setError(null);
    setMessages((current) => [...current,
      { id: localUserId, role: "User", content: trimmed, status: "Completed", citations: [], webEvidenceSnapshots: [], toolExecutions: [], createdAt: new Date().toISOString() },
      { id: localAssistantId, role: "Assistant", content: "", status: "Pending", activity: "Analyzing the question", citations: [], webEvidenceSnapshots: [], toolExecutions: [], createdAt: new Date().toISOString() },
    ]);
    if (clearComposer) setQuestion("");

    let activeConversationId = preferredConversationId ?? conversationId;
    const abortController = new AbortController();
    activeChatAbortRef.current = abortController;
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
      }, abortController.signal);
      if (!completed) throw new Error("Ask RAVEN ended before returning a completed response.");
      if (clearComposer) {
        try { localStorage.removeItem(draftAtSend); } catch { /* Optional draft storage. */ }
      }
      void listChatConversations(companyId).then(setRecentChats).catch(() => undefined);
    } catch (submissionError) {
      if (isIntentionalAbort(submissionError, abortController.signal)) {
        setMessages((current) => current.map((message) => message.id === localAssistantId && message.status === "Pending" ? { ...message, content: "Response stopped.", status: "Failed", activity: null } : message));
        return;
      }
      setMessages((current) => current.map((message) => message.id === localAssistantId && message.status === "Pending" ? { ...message, content: "Ask RAVEN could not complete this response.", status: "Failed", activity: null } : message));
      setError(submissionError instanceof Error ? submissionError.message : "Ask RAVEN could not complete this request.");
      if (clearComposer) setQuestion(trimmed);
    } finally {
      if (activeChatAbortRef.current === abortController) activeChatAbortRef.current = null;
      if (activeConversationId && !requestedConversationId) setConversationInUrl(activeConversationId);
      setPending(false);
    }
  };

  const retryMessage = (content: string) => {
    if (!pending && content.trim()) void sendNormalQuestion(content.trim(), conversationId, false);
  };

  const beginEditingMessage = (message: ChatMessage) => {
    if (pending) return;
    setEditingMessageId(message.id);
    setEditingMessageText(message.content);
  };

  const submitEditedMessage = () => {
    const next = editingMessageText.trim();
    if (!next || pending) return;
    setEditingMessageId(null);
    setEditingMessageText("");
    void sendNormalQuestion(next, conversationId, false);
  };

  const submitQuestion = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (speech.listening) return;
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
    await sendNormalQuestion(trimmed, conversationId, true);
  };

  const confirmNextAction = async () => {
    const action = nextActionReview;
    const objective = action?.value.trim();
    if (!action || !objective || pending || nextActionBusy) return;
    setNextActionBusy(true);
    setError(null);
    try {
      if (action.kind === "latest") {
        const targetConversationId = await setWebSearchCapability(true);
        if (!targetConversationId) return;
        setNextActionReview(null);
        await sendNormalQuestion(objective, targetConversationId, false);
      } else {
        const job = await startManagedResearch(companyId, objective, { purpose: "General" });
        addManagedResearchJob(job);
        setNextActionReview(null);
      }
    } catch (actionError) {
      setError(actionError instanceof Error ? actionError.message : "The follow-up could not be started.");
    } finally {
      setNextActionBusy(false);
    }
  };
  const capabilitiesMenu = capabilitiesOpen ? <AskRavenCapabilityMenu id={capabilitiesId} hasProfile={!!profileVersionId}
    pickerOpen={investigationPickerOpen} onPickerOpen={setInvestigationPickerOpen} search={investigationSearch} onSearch={setInvestigationSearch}
    investigations={investigations} briefings={briefings} attachments={researchContextAttachments} attachingId={attachingResearchId}
    webSearchEnabled={webSearchEnabled} capabilitySaving={capabilitySaving} pending={pending} conversationLoading={conversationLoading}
    activeDeepResearch={activeCapability === "deepResearch"} firstActionRef={addResearchContextActionRef}
    onAttachInvestigation={(job) => void attachInvestigation(job)} onAttachBriefing={(briefing) => void attachBriefing(briefing)}
    onRemoveAttachment={(attachment) => void removeAttachment(attachment)}
    onWebSearchChange={(enabled) => void setWebSearchCapability(enabled)}
    onDeepResearch={() => {
      setActiveCapability("deepResearch");
      setDeepResearchBrief(null);
      setDeepResearchOriginalQuestion(null);
      setDeepResearchBriefEditing(false);
      setCapabilitiesOpen(false);
      window.setTimeout(() => questionInputRef.current?.focus(), 0);
    }} /> : null;
  return (
    <section className={styles.handoff} data-testid="ask-raven-handoff" aria-labelledby="ask-raven-heading">
      <AskRavenHeader companyName={companyName} sourceCount={sourceCount} lastResearchedAt={lastResearchedAt} hasProfile={!!profileVersionId}
        busy={pending || conversationLoading} recentChats={recentChats} recentChatsOpen={recentChatsOpen} activeConversationId={conversationId}
        onToggleRecent={() => setRecentChatsOpen((open) => !open)} onSelectRecent={selectRecentChat} onNewChat={startNewConversation} onDeleteChat={(id) => void deleteConversation(id)} />
      {conversationId && conversationProfileVersionId && profileVersionId && conversationProfileVersionId !== profileVersionId ? <div className={styles.newerProfileNotice}>
        Newer Company Profile available. <button type="button" onClick={startNewConversation}>Start new chat with current profile</button>
      </div> : null}

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
          <article key={message.id} className={`${styles.chatMessage} ${message.role === "User" ? `${styles.chatMessageUser} chat-message-user${editingMessageId === message.id ? ` ${styles.chatMessageEditing}` : ""}` : styles.chatMessageAssistant}`}>
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
        {speech.error ? <div className={styles.chatSystemMessage} role="alert">{speech.error}</div> : null}
      </div>
      {showLatest ? <button type="button" className={styles.latestButton} onClick={() => chatViewportRef.current?.scrollTo({ top: chatViewportRef.current.scrollHeight, behavior: "smooth" })}><ArrowDown size={14} aria-hidden="true" /> Latest</button> : null}
      </div>

      <AskRavenComposer
        companyName={companyName}
        question={question}
        onQuestionChange={setQuestion}
        onSubmit={submitQuestion}
        onStop={() => activeChatAbortRef.current?.abort()}
        questionInputRef={questionInputRef}
        profileVersionId={profileVersionId ?? null}
        pending={pending}
        conversationLoading={conversationLoading}
        deepResearchBriefActive={deepResearchBrief !== null}
        deepResearchBriefLoading={deepResearchBriefLoading}
        deepResearchStarting={deepResearchStarting}
        activeCapability={activeCapability}
        onRemoveDeepResearch={() => { researchBriefVersionRef.current += 1; setDeepResearchBriefLoading(false); setActiveCapability(null); setDeepResearchBrief(null); setDeepResearchOriginalQuestion(null); setDeepResearchBriefEditing(false); }}
        webSearchEnabled={webSearchEnabled}
        researchContextAttachments={researchContextAttachments}
        contextDetailsOpen={contextDetailsOpen}
        onToggleContextDetails={() => setContextDetailsOpen((open) => !open)}
        onRemoveAttachment={(attachment) => void removeAttachment(attachment)}
        onClearAllAttachments={() => researchContextAttachments.forEach((attachment) => { void removeAttachment(attachment); })}
        capabilitiesOpen={capabilitiesOpen}
        capabilitiesId={capabilitiesId}
        capabilitiesRef={capabilitiesRef}
        capabilitiesTriggerRef={capabilitiesTriggerRef}
        onToggleCapabilities={() => { if (capabilitiesOpen) setInvestigationPickerOpen(false); setCapabilitiesOpen((open) => !open); }}
        capabilitiesMenu={capabilitiesMenu}
        speech={speech}
        onStartSpeech={(preferences) => void speech.start(preferences)}
      />
    </section>
  );
}
