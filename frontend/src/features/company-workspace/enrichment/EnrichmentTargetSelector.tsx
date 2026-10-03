import { ArrowSquareOut, Sparkle } from "@phosphor-icons/react";
import type { ResearchTarget } from "../../../api/coverage";
import { hasUsableAcceptedProfile } from "../../../utils/profileReadiness";
import type { DossierProfile } from "../dossierTypes";
import { targetLabels, targetOrder } from "./enrichmentMapping";
import styles from "../company-workspace.module.css";

type Props = {
  targets: ResearchTarget[];
  toggleTarget: (target: ResearchTarget) => void;
  profile?: DossierProfile | null;
  loading: boolean;
  start: () => Promise<void>;
  launchDeepResearch: () => Promise<void>;
  onClose: () => void;
  onOpenExternalResearch?: (targets: ResearchTarget[]) => void;
  close: () => void;
};

export function EnrichmentTargetSelector({ targets, toggleTarget, profile, loading, start, launchDeepResearch, onClose, onOpenExternalResearch, close }: Props) {
  return (
<section className={styles.enrichmentSection} aria-labelledby="targeted-areas-heading">
          <h3 id="targeted-areas-heading">What should RAVEN strengthen?</h3>
          <p className={styles.contextNote}>Choose one or more missing or weak areas. Unselected accepted fields cannot be changed by this run.</p>
          <div className={styles.targetGrid}>
            {targetOrder.map((target) => <label className={styles.targetOption} key={target}><input type="checkbox" checked={targets.includes(target)} onChange={() => toggleTarget(target)} /><span><strong>{targetLabels[target]}</strong><small>Target-specific evidence only</small></span></label>)}
          </div>
            <div className={styles.methodChoice} aria-labelledby="research-method-heading">
              <div><p className={styles.eyebrow}>RESEARCH METHOD</p><h3 id="research-method-heading">Improve {targets.length === 1 ? targetLabels[targets[0]] : "selected areas"}</h3><p className={styles.contextNote}>RAVEN Research is the fast integrated evidence workflow. Deep Research runs asynchronously and becomes reviewable for profile improvement after an accepted Profile v1 exists. External AI Assist brings in outside material for review.</p></div>
              <article className={styles.methodChoiceRecommended}><div><strong>Research with RAVEN</strong><span>Search and verify sources using RAVEN's normal evidence workflow.</span></div><button className="button" type="button" onClick={() => void start()} disabled={loading || targets.length === 0 || !hasUsableAcceptedProfile(profile)}>Find selected information</button></article>
              {!hasUsableAcceptedProfile(profile) ? <p className={styles.contextNote} role="status">Create or repair the initial profile before applying targeted evidence. Deep Research may still run in the background, but its completed result stays locked until a supported profile exists.</p> : null}
              <div className={styles.methodChoiceAlternatives}><button className="button button--secondary" type="button" onClick={() => void launchDeepResearch()} disabled={loading || targets.length === 0}><Sparkle size={16} weight="fill" /> Deep Research <small>Broader background investigation</small></button><button className="button button--secondary" type="button" onClick={() => { if (targets.length > 0) { onClose(); onOpenExternalResearch?.(targets); } }} disabled={loading || targets.length === 0}><ArrowSquareOut size={16} weight="bold" /> External AI Assist <small>Generate a brief for all selected areas</small></button></div>
            </div>
          <div className="form-actions"><button className="button button--secondary" type="button" onClick={close}>Cancel</button></div>
        </section>
  );
}
