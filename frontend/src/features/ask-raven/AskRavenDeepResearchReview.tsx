import type { Dispatch, SetStateAction } from "react";
import type { ManagedResearchBriefPreview } from "../../api/managedResearch";
import styles from "./ask-raven.module.css";

type Props = {
  deepResearchBrief: ManagedResearchBriefPreview;
  deepResearchBriefEditing: boolean;
  setDeepResearchBriefEditing: Dispatch<SetStateAction<boolean>>;
  setDeepResearchBrief: Dispatch<SetStateAction<ManagedResearchBriefPreview | null>>;
  deepResearchStarting: boolean;
  companyName: string;
  profileVersionId?: string | null;
  profileVersion?: number | null;
  sourceCount: number;
  cancelDeepResearchBrief: () => void;
  startDeepResearchBrief: () => Promise<void>;
};

export function AskRavenDeepResearchReview({ deepResearchBrief, deepResearchBriefEditing, setDeepResearchBriefEditing, setDeepResearchBrief, deepResearchStarting, companyName, profileVersionId, profileVersion, sourceCount, cancelDeepResearchBrief, startDeepResearchBrief }: Props) {
  return (
<section className={styles.researchBriefCard} data-testid="deep-research-brief" aria-labelledby="deep-research-brief-heading">
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
        </section>
  );
}
