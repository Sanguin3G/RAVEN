import { FormEvent, useState } from "react";
import { ArrowUp, CaretDown, Plus, Sparkle } from "@phosphor-icons/react";
import styles from "./dossier.module.css";

export interface AskRavenHandoffProps {
  companyId: string;
  companyName: string;
  profileVersion?: number | null;
  sourceCount: number;
  lastResearchedAt?: string | null;
}

type AssistantMode = "quick" | "deep";

function formatDate(value?: string | null): string | null {
  if (!value) return null;
  const timestamp = Date.parse(value);
  return Number.isNaN(timestamp) ? null : new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short" }).format(timestamp);
}

export function AskRavenHandoff({ companyId, companyName, profileVersion, sourceCount, lastResearchedAt }: AskRavenHandoffProps) {
  const [mode, setMode] = useState<AssistantMode>("quick");
  const [question, setQuestion] = useState("");
  const [handoffMessage, setHandoffMessage] = useState<string | null>(null);
  const researched = formatDate(lastResearchedAt);

  const submitQuestion = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!question.trim()) return;

    // Hung owns the production conversation contract. Keep this truthful until
    // the adapter can call that contract instead of inventing a reply.
    setHandoffMessage("Ask RAVEN backend integration is pending. Your question was not sent.");
  };

  return (
    <section className={`${styles.handoff} ${mode === "deep" ? styles.handoffDeep : ""}`} data-testid="ask-raven-handoff" aria-labelledby="ask-raven-heading">
      <header className={styles.assistantCompactHeader}>
        <span aria-hidden="true" className={styles.aiMark}>AI</span>
        <div>
          <h2 id="ask-raven-heading">Ask RAVEN</h2>
          <p title={companyId}>{companyName}<span aria-hidden="true"> · </span>{profileVersion ? `v${profileVersion}` : "No profile"}<span aria-hidden="true"> · </span>{sourceCount} sources{researched ? <><span aria-hidden="true"> · </span>{researched}</> : null}</p>
        </div>
        <span className={styles.handoffStatus}>Pending</span>
      </header>

      <div className={styles.chatViewport} aria-live="polite" aria-label="Ask RAVEN conversation">
        <div className={styles.chatEmptyState}>
          <span className={styles.chatEmptyMark} aria-hidden="true">✦</span>
          <strong>{mode === "deep" ? "Deep Research is ready" : "Ask about this company"}</strong>
          <p>{mode === "deep" ? "Choose Deep Research when the question needs several public sources." : "Company context is included automatically."}</p>
        </div>
        {handoffMessage ? <div className={styles.chatSystemMessage} role="status">{handoffMessage}</div> : null}
      </div>

      <form className={styles.assistantComposer} onSubmit={submitQuestion}>
        <label className="sr-only" htmlFor="ask-raven-question">{mode === "deep" ? "What should RAVEN investigate?" : `Ask about ${companyName}`}</label>
        <textarea
          id="ask-raven-question"
          value={question}
          onChange={(event) => { setQuestion(event.target.value); setHandoffMessage(null); }}
          onKeyDown={(event) => {
            if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) {
              event.preventDefault();
              event.currentTarget.form?.requestSubmit();
            }
          }}
          placeholder={mode === "deep" ? "Investigate this company…" : `Ask about ${companyName}…`}
          rows={2}
        />
        <div className={styles.assistantComposerFooter}>
          <button className={styles.assistantAttach} type="button" disabled title="Attachments will be available with Ask RAVEN backend integration" aria-label="Attachments unavailable"><Plus size={17} weight="bold" /></button>
          <label className={styles.assistantModeSelect}>
            {mode === "deep" ? <Sparkle size={14} weight="fill" aria-hidden="true" /> : null}
            <select value={mode} onChange={(event) => { setMode(event.target.value as AssistantMode); setHandoffMessage(null); }} aria-label="Ask RAVEN mode">
              <option value="quick">Quick</option>
              <option value="deep">Deep Research</option>
            </select>
            <CaretDown size={13} weight="bold" aria-hidden="true" />
          </label>
          {question.trim() ? <button className={styles.assistantSubmit} type="submit" aria-label={mode === "deep" ? "Start Deep Research" : "Send question"}>
            <ArrowUp size={17} weight="bold" aria-hidden="true" />
          </button> : null}
        </div>
      </form>
      <p className={styles.assistantFootnote}>Quick is the default. Deep changes the research budget when Hung's backend is connected.</p>
    </section>
  );
}
