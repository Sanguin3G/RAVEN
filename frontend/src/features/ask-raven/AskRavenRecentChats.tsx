import { useState } from "react";
import { DotsThree } from "@phosphor-icons/react";
import type { ChatConversationSummary } from "../../types/chat";
import styles from "./ask-raven.module.css";

interface Props {
  recentChats: ChatConversationSummary[];
  activeConversationId: string | null;
  onSelectRecent: (id: string) => void;
  onDeleteChat: (id: string) => void;
}

export function AskRavenRecentChats({ recentChats, activeConversationId, onSelectRecent, onDeleteChat }: Props) {
  const [openOverflowId, setOpenOverflowId] = useState<string | null>(null);

  return <div className={styles.recentChatsPanel} aria-label="Recent chats">
    <strong>Recent chats</strong>
    {recentChats.length ? recentChats.map((chat) => <div className={styles.recentChatRow} key={chat.id}>
      <button className={styles.recentChatSelect} type="button" aria-current={chat.id === activeConversationId ? "true" : undefined} onClick={() => onSelectRecent(chat.id)}>
        <span>{chat.title || "New chat"}</span>
        <small>{chat.messageCount} messages · {new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short" }).format(Date.parse(chat.updatedAt))}</small>
      </button>
      <div className={styles.recentChatOverflow}>
        <button type="button" aria-label={`Options for ${chat.title || "New chat"}`} aria-expanded={openOverflowId === chat.id} onClick={() => setOpenOverflowId((current) => current === chat.id ? null : chat.id)}><DotsThree size={18} weight="bold" aria-hidden="true" /></button>
        {openOverflowId === chat.id ? <div className={styles.recentChatActions}><button type="button" onClick={() => { setOpenOverflowId(null); onDeleteChat(chat.id); }}>Delete chat</button></div> : null}
      </div>
    </div>) : <p>No previous chats for this company.</p>}
  </div>;
}
