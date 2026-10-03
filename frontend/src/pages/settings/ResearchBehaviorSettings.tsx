import { Panel } from "../../components/Panel";
import type { GroundingMode, ResearchSettings } from "../../api/settings";
import styles from "../settings.module.css";

const groundingChoices: Array<{ value: GroundingMode; title: string; description: string }> = [
  { value: "Auto", title: "Smart matching · Recommended", description: "Use AI only when the company identity is ambiguous." },
  { value: "Always", title: "Always verify", description: "Ask AI to verify every research target before searching." },
  { value: "Off", title: "Deterministic only", description: "Skip AI matching and use the supplied identity directly." },
];

type Props = {
  draft: ResearchSettings;
  disabled: boolean;
  updateDraft: (changes: Partial<ResearchSettings>) => void;
};

export function ResearchBehaviorSettings({ draft, disabled, updateDraft }: Props) {
  return (
<Panel title="Company matching" eyebrow="RESEARCH BEHAVIOR" className={styles.section}>
            <div className={styles.sectionIntro}><p>How carefully should RAVEN verify which company you mean before searching?</p></div>
            <fieldset className={styles.fieldSet} disabled={disabled}>
              <legend>Matching behavior</legend><p className={styles.fieldHint}>This default applies to new research. A research run can override it for that request.</p>
              <div className={styles.choiceGrid}>{groundingChoices.map(choice => <label className={styles.choice} key={choice.value}><input type="radio" name="grounding-mode" value={choice.value} checked={draft.groundingMode === choice.value} onChange={() => updateDraft({ groundingMode: choice.value })} /><span className={styles.choiceCopy}><strong>{choice.title}</strong><small>{choice.description}</small></span></label>)}</div>
            </fieldset>
            <label className={styles.toggleRow}><span className={styles.toggleCopy}><strong>AI-assisted source ranking</strong><span>Prioritize sources that better match the resolved company identity.</span></span><input className={styles.toggle} type="checkbox" role="switch" checked={draft.aiSourceRerankingEnabled} disabled={disabled} onChange={event => updateDraft({ aiSourceRerankingEnabled: event.target.checked })} aria-label="AI-assisted source ranking" /></label>
          </Panel>
  );
}
