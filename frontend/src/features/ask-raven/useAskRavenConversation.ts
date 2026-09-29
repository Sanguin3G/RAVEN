import { useEffect, useRef, useState } from "react";
import type { SetURLSearchParams } from "react-router-dom";
import { getChatConversation, listChatConversations } from "../../api/chat";
import type { ChatConversationResponse, ChatConversationSummary, ChatMessage } from "../../types/chat";
import { getActiveChat, setActiveChat } from "./chatBrowserState";

type Options = {
  companyId: string;
  requestedConversationId: string | null;
  searchParams: URLSearchParams;
  setSearchParams: SetURLSearchParams;
  onCompanyChanged: () => void;
  onConversationSwitch: () => void;
  onRestoreStart: () => void;
};

export function useAskRavenConversation({ companyId, requestedConversationId, searchParams, setSearchParams, onCompanyChanged, onConversationSwitch, onRestoreStart }: Options) {
  const [conversationId, setConversationId] = useState<string | null>(requestedConversationId);
  const [conversationProfileVersionId, setConversationProfileVersionId] = useState<string | null>(null);
  const [recentChats, setRecentChats] = useState<ChatConversationSummary[]>([]);
  const [recentChatsOpen, setRecentChatsOpen] = useState(false);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [webSearchEnabled, setWebSearchEnabled] = useState(false);
  const [conversationLoading, setConversationLoading] = useState(false);
  const activeCompanyIdRef = useRef(companyId);
  const hydratedConversationRef = useRef<string | null>(null);
  const skipFallbackRef = useRef(false);

  const setConversationInUrl = (nextConversationId: string | null) => {
    const next = new URLSearchParams(searchParams);
    if (nextConversationId) next.set("conversation", nextConversationId);
    else next.delete("conversation");
    setSearchParams(next, { replace: true });
  };

  useEffect(() => {
    let active = true;
    const companyChanged = activeCompanyIdRef.current !== companyId;
    activeCompanyIdRef.current = companyId;
    if (!companyChanged && requestedConversationId && hydratedConversationRef.current === requestedConversationId) return () => { active = false; };
    if (!companyChanged && !requestedConversationId && (conversationId || skipFallbackRef.current)) return () => { active = false; };
    if (companyChanged) {
      onCompanyChanged();
      hydratedConversationRef.current = null;
      skipFallbackRef.current = false;
      setConversationId(null);
      setMessages([]);
      setWebSearchEnabled(false);

    }
    setConversationLoading(true);
    onRestoreStart();
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
          onConversationSwitch();
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


  return { conversationId, setConversationId, conversationProfileVersionId, setConversationProfileVersionId,
    recentChats, setRecentChats, recentChatsOpen, setRecentChatsOpen, messages, setMessages,
    webSearchEnabled, setWebSearchEnabled, conversationLoading, setConversationLoading,
    activeCompanyIdRef, hydratedConversationRef, skipFallbackRef, setConversationInUrl };
}
