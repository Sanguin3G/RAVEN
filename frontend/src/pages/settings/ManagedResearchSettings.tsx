import { Panel } from "../../components/Panel";
import type { ProviderStatusResponse } from "../../api/system";
import type { ManagedResearchDepth, ResearchSettings } from "../../api/settings";
import styles from "../settings.module.css";

const managedResearchDepthChoices: Array<{ value: ManagedResearchDepth; title: string; description: string }> = [
  { value: "Adaptive", title: "Adaptive", description: "Let managed research choose an appropriate effort for the question." },
  { value: "Focused", title: "Focused", description: "A compact investigation for a narrow target." },
  { value: "Standard", title: "Standard", description: "Balanced breadth and depth for most investigations." },
  { value: "Thorough", title: "Thorough", description: "Broader source coverage for consequential questions." },
  { value: "Exhaustive", title: "Exhaustive", description: "The deepest available investigation; use selectively." },
];

type Props = {
  draft: ResearchSettings;
  disabled: boolean;
  providers: ProviderStatusResponse | null;
  updateDraft: (changes: Partial<ResearchSettings>) => void;
};

export function ManagedResearchSettings({ draft, disabled, providers, updateDraft }: Props) {
  return (
<Panel title="Managed Deep Research" eyebrow="EXA AGENT" className={styles.section}>
            <div className={styles.sectionIntro}><p>Managed Deep Research runs asynchronously through Exa Agent and returns a reviewable Investigation. Exa controls its underlying model; Gemini model choices do not change Exa Agent. Managed research does not update the accepted Company Profile automatically.</p></div>
            <div className={styles.researchProvider}><span className={`${styles.statusDot} ${providers?.exa?.configured ? styles["statusDot--checking"] : styles["statusDot--warning"]}`} aria-hidden="true" /><div><strong>Exa Agent</strong><small>{providers?.exa?.configured ? "Configured · Exa chooses the model for managed multi-step research" : "Not configured · add an Exa API key to enable"}</small></div></div>
            <label className={styles.depthControl} htmlFor="managed-research-depth"><strong>Default research depth</strong><small>Choose the breadth of new managed Investigations.</small><select id="managed-research-depth" value={draft.managedResearchDepth} disabled={disabled} onChange={event => updateDraft({ managedResearchDepth: event.target.value as ManagedResearchDepth })}>{managedResearchDepthChoices.map(choice => <option key={choice.value} value={choice.value}>{choice.title}</option>)}</select><small>{managedResearchDepthChoices.find(choice => choice.value === draft.managedResearchDepth)?.description}</small></label>
          </Panel>
  );
}
