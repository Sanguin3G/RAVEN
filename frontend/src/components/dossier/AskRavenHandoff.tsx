import { FormEvent, useState } from "react";
import styles from "./dossier.module.css";

export interface AskRavenHandoffProps {
  companyId: string;
  companyName: string;
  profileVersion?: number | null;
  sourceCount: number;
  lastResearchedAt?: string | null;
}

type AssistantMode = "quick" | "deep";

function display(value?: number | string | null): string {
  if (value === null || value === undefined || value === "") return "Not available";
  return String(value);
}

function formatDate(value?: string | null): string {
  if (!value) return "Not available";
  const timestamp = Date.parse(value);
  return Number.isNaN(timestamp) ? "Not available" : new Intl.DateTimeFormat(undefined, { dateStyle: "medium" }).format(timestamp);
}

export function AskRavenHandoff({ companyId, companyName, profileVersion, sourceCount, lastResearchedAt }: AskRavenHandoffProps) {
  const [mode, setMode] = useState<AssistantMode>("quick");
  const [question, setQuestion] = useState("");
  const [handoffMessage, setHandoffMessage] = useState<string | null>(null);

  const submitQuestion = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!question.trim()) return;

    // Hung owns the production Ask RAVEN backend. Until its contract is
    // connected here, keep this interaction explicit instead of fabricating a
    // response or simulating citations/activity.
    setHandoffMessage("Ask RAVEN backend integration is pending. Your question was not sent.");
  };

  return (
    <section className={styles.handoff} data-testid="ask-raven-handoff" aria-labelledby="ask-raven-heading">
      <div className={styles.handoffHeader}>
        <span aria-hidden="true" className={styles.aiMark}>AI</span>
        <div className={styles.handoffTitle}>
          <p className={styles.eyebrow}>Company-scoped assistant</p>
          <h2 id="ask-raven-heading">Ask RAVEN</h2>
          <span className={styles.handoffStatus}>Integration pending</span>
        </div>
      </div>

      <div className={styles.assistantCompanyContext}>
        <strong>{companyName}</strong>
        <span>RAVEN already has this company context</span>
      </div>

      <dl className={styles.handoffContext} aria-label="Ask RAVEN context">
        <div><dt>Company ID</dt><dd>{display(companyId)}</dd></div>
        <div><dt>Profile version</dt><dd>{display(profileVersion)}</dd></div>
        <div><dt>Stored sources</dt><dd>{sourceCount}</dd></div>
        <div><dt>Last researched</dt><dd>{formatDate(lastResearchedAt)}</dd></div>
      </dl>

      <fieldset className={styles.assistantModes} aria-label="Research mode">
        <button
          className={`${styles.assistantMode} ${mode === "quick" ? styles.assistantModeActive : ""}`}
          type="button"
          aria-pressed={mode === "quick"}
          onClick={() => { setMode("quick"); setHandoffMessage(null); }}
        >
          <span>Quick</span>
          <small>Focused answer</small>
        </button>
        <button
          className={`${styles.assistantMode} ${mode === "deep" ? styles.assistantModeActiveDeep : ""}`}
          type="button"
          aria-pressed={mode === "deep"}
          onClick={() => { setMode("deep"); setHandoffMessage(null); }}
        >
          <span aria-hidden="true">✦</span> <span>Deep Research</span>
          <small>Search, read, cross-check</small>
        </button>
      </fieldset>

      <div className={styles.assistantModeIntro}>
        <strong>{mode === "deep" ? "Deep Research" : "Quick Ask"}</strong>
        <span>{mode === "deep" ? "Several public sources can be reviewed once the research backend is connected." : "Ask a focused question about this company once the assistant backend is connected."}</span>
      </div>

      <form className={styles.assistantComposer} onSubmit={submitQuestion}>
        <label htmlFor="ask-raven-question">{mode === "deep" ? "What should RAVEN investigate?" : `Ask about ${companyName}`}</label>
        <textarea
          id="ask-raven-question"
          value={question}
          onChange={(event) => { setQuestion(event.target.value); setHandoffMessage(null); }}
          placeholder={mode === "deep" ? "Compare markets, leadership, products, or recent public activity…" : `Ask about ${companyName}…`}
          rows={3}
          aria-describedby="ask-raven-composer-hint"
        />
        <div className={styles.assistantComposerFooter}>
          <span id="ask-raven-composer-hint">Company context is included automatically.</span>
          <button className={styles.assistantSubmit} type="submit" disabled={!question.trim()}>
            {mode === "deep" ? "Start research" : "Send"}
          </button>
        </div>
      </form>

      {handoffMessage && <p className={styles.assistantPending} role="status">{handoffMessage}</p>}

      <p className={styles.assistantFootnote}>Responses, citations, activity, and Save Investigation will appear here after Hung’s backend contract is connected.</p>
    </section>
  );
}
