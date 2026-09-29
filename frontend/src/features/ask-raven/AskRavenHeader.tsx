import { Plus } from "@phosphor-icons/react";
import type { ChatConversationSummary } from "../../types/chat";
import { AskRavenMark } from "./AskRavenMark";
import { AskRavenRecentChats } from "./AskRavenRecentChats";
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
      {recentChatsOpen ? <AskRavenRecentChats recentChats={recentChats} activeConversationId={activeConversationId} onSelectRecent={onSelectRecent} onDeleteChat={onDeleteChat} /> : null}
    </div>
  </header>;
}
