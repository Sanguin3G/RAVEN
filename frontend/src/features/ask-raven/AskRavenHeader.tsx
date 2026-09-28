import { useState } from "react";
import { DotsThree, Plus } from "@phosphor-icons/react";
import type { ChatConversationSummary } from "../../types/chat";
import { AskRavenMark } from "./AskRavenMark";
import styles from "./ask-raven.module.css";

interface Props {
  companyName: string;
  sourceCount: number;
  lastResearchedAt?: string | null;
  hasProfile: boolean;
  busy: boolean;
  recentChats: ChatConversationSummary[];
  recentChatsOpen: boolean;
  activeConversationId: string | null;
  onToggleRecent: () => void;
  onSelectRecent: (id: string) => void;
  onNewChat: () => void;
  onDeleteChat: (id: string) => void;
}

export function AskRavenHeader({ companyName, sourceCount, lastResearchedAt, hasProfile, busy, recentChats, recentChatsOpen, activeConversationId, onToggleRecent, onSelectRecent, onNewChat, onDeleteChat }: Props) {
  const [openOverflowId, setOpenOverflowId] = useState<string | null>(null);
  const researched = lastResearchedAt && !Number.isNaN(Date.parse(lastResearchedAt))
    ? new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short" }).format(Date.parse(lastResearchedAt)) : null;

  return <header className={styles.assistantCompactHeader}>
    <AskRavenMark />
    <div className={styles.headerIdentity}>
      <h2 id="ask-raven-heading">Ask RAVEN</h2>
      <strong className={styles.headerCompany}>{companyName}</strong>
      <div className={styles.headerGrounding}>
        <span>{hasProfile ? "● Profile grounded" : "Profile required"}</span>
        <span>{sourceCount} sources</span>
        {researched ? <span>Updated {researched}</span> : null}
      </div>
    </div>
    <div className={styles.headerActions}>
      <button type="button" className={styles.recentChatsButton} aria-expanded={recentChatsOpen} onClick={onToggleRecent}>Recent chats</button>
      <button className={styles.newConversationButton} type="button" onClick={onNewChat} disabled={busy} aria-label="New conversation">
        <Plus size={14} weight="bold" aria-hidden="true" /><span>New chat</span>
      </button>
      {recentChatsOpen ? <div className={styles.recentChatsPanel} aria-label="Recent chats">
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
        <button type="button" className={styles.recentNewChat} onClick={onNewChat}>+ New chat</button>
      </div> : null}
    </div>
  </header>;
}
