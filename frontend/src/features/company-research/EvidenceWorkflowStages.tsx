import { Button } from "../../components/Button";
import { Panel } from "../../components/Panel";
import {
  CandidateSourceCard,
  EvidenceCard,
} from "../../components/sources";
import type { CompanyResearchWorkflow } from "./types";
import { toCandidateSource, toEvidenceRecord, targetLabel } from "./formatters";
import styles from "./research-workspace.module.css";

export function CandidateReviewStage({ workflow }: { workflow: CompanyResearchWorkflow }) {
  const { view, candidates, run, selectedCount, loading, isPaused, selectionError } = workflow;
  if (view !== "reviewingSources" && view !== "acquiring") return null;

  return (
    <Panel title="Review source candidates" eyebrow="STEP 03 · SOURCE SELECTION" className={styles.sourcesPanel}>
      <div className={styles.sectionSummary}>
        <div><strong>{candidates.length} unique source{candidates.length === 1 ? "" : "s"}</strong><p>{run?.recommendedCandidates ?? 0} recommended by RAVEN · {selectedCount} selected</p></div>
        <span className={styles.selectionPill}>{selectedCount} selected</span>
      </div>
      {candidates.length > 0 ? (
        <div className={styles.candidateGrid}>
          {candidates.map((candidate) => (
            <CandidateSourceCard key={candidate.id} candidate={toCandidateSource(candidate)} disabled={loading || isPaused} onSelectionChange={(selected) => workflow.updateCandidateSelection(candidate.id, selected)} />
          ))}
        </div>
      ) : (
        <p className="empty-state">No public source candidates were found. Try another identity or research hint.</p>
      )}
      {selectionError ? <p className="form-error" role="alert">{selectionError}</p> : null}
      <div className="form-actions">
        <Button type="button" onClick={() => void workflow.handleAcquire()} loading={view === "acquiring"} disabled={candidates.length === 0 || isPaused}>Acquire {selectedCount} selected source{selectedCount === 1 ? "" : "s"}</Button>
      </div>
    </Panel>
  );
}

export function EvidenceReviewStage({ workflow, onOpenExternalResearch }: { workflow: CompanyResearchWorkflow; onOpenExternalResearch?: () => void }) {
  const { view, sources, run, coverage, candidates, coverageGaps, strengtheningTargets, strengthenMethod, loading, isPaused } = workflow;
  if (view !== "reviewingEvidence") return null;

  const failedCandidates = candidates.filter((candidate) => candidate.acquisitionStatus === "Failed" || candidate.acquisitionStatus === "Unavailable" || candidate.acquisitionStatus === "DuplicateSkipped");
  return (
    <Panel title="Evidence ready" eyebrow="STEP 04 · REVIEW EVIDENCE" className={styles.evidencePanel}>
      <div className={styles.sectionSummary}>
        <div><strong>{sources.length} acquired source{sources.length === 1 ? "" : "s"}</strong><p>Review the public pages RAVEN preserved before generating a Company Profile.</p></div>
        <span className={styles.successPill}>{run?.documentsAdded ?? sources.length} documents added</span>
      </div>
      {coverage ? <section className={styles.coverageSummary} aria-label="Research coverage"><h3>Research coverage</h3><ul>{coverage.items.map((item) => <li key={item.target}><span>{targetLabel(item.target)}</span><strong>{item.level}</strong></li>)}</ul>{coverage.budgetExhausted ? <p>Research budget reached. Remaining gaps stay unknown.</p> : null}</section> : null}
      {sources.length > 0 ? <div className={styles.evidenceGrid}>{sources.map((source) => <EvidenceCard key={source.id} evidence={toEvidenceRecord(source)} />)}</div> : <p className="empty-state">No source documents were acquired. The selected sources may have been unavailable.</p>}
      {failedCandidates.length > 0 ? (
        <div className={styles.acquisitionIssues} role="status">
          <h3>Sources not added as evidence</h3>
          <ul>
            {failedCandidates.map((candidate) => (
              <li key={candidate.id}><strong>{candidate.title || candidate.domain}</strong> — {candidate.acquisitionStatus === "DuplicateSkipped" ? "duplicate content skipped" : candidate.acquisitionError || "source unavailable"}</li>
            ))}
          </ul>
        </div>
      ) : null}
      {coverageGaps.length > 0 ? <fieldset className={styles.coverageTargets}><legend>Areas to strengthen</legend>{coverageGaps.map((target) => <label key={target}><input type="checkbox" checked={strengtheningTargets.includes(target)} onChange={() => workflow.toggleStrengtheningTarget(target)} /> {targetLabel(target)}</label>)}</fieldset> : null}
      <div className={styles.profileNextStep}>
        <div><p className="eyebrow">NEXT · STRENGTHEN DOSSIER</p><h3>Choose how to research the remaining gaps</h3><p>RAVEN Search → Crawl is recommended. Deep Research and External AI Assist are alternatives; choose one method for this pass.</p></div>
        <fieldset className={styles.strengthenMethodChooser} aria-label="Strengthen Dossier method">
          <legend>Research method</legend>
          <label className={strengthenMethod === "raven" ? styles.strengthenMethodSelected : styles.strengthenMethod}><input type="radio" name="strengthen-method" value="raven" checked={strengthenMethod === "raven"} onChange={() => workflow.setStrengthenMethod("raven")} /><span><strong>RAVEN Research <em>Recommended</em></strong><small>Fast integrated search, review, and crawl into profile evidence.</small></span></label>
          <label className={strengthenMethod === "deep" ? styles.strengthenMethodSelected : styles.strengthenMethod}><input type="radio" name="strengthen-method" value="deep" checked={strengthenMethod === "deep"} onChange={() => workflow.setStrengthenMethod("deep")} /><span><strong>Deep Research</strong><small>Broader async investigation. It does not block the profile; create Profile v1 before review or improvement.</small></span></label>
          <label className={strengthenMethod === "external" ? styles.strengthenMethodSelected : styles.strengthenMethod}><input type="radio" name="strengthen-method" value="external" checked={strengthenMethod === "external"} onChange={() => workflow.setStrengthenMethod("external")} /><span><strong>External AI Assist</strong><small>Bring back findings from another web-enabled assistant.</small></span></label>
        </fieldset>
        <div className="form-actions"><Button type="button" onClick={() => strengthenMethod === "raven" ? void workflow.handleStrengthenDossier() : strengthenMethod === "deep" ? void workflow.handleDeepResearch() : onOpenExternalResearch?.()} loading={loading} disabled={strengtheningTargets.length === 0 || isPaused || (strengthenMethod === "external" && !onOpenExternalResearch)}>{strengthenMethod === "raven" ? "Start RAVEN research" : strengthenMethod === "deep" ? "Start Deep Research" : "Open External AI Assist"}</Button><Button type="button" onClick={() => void workflow.handleGenerateProfile()} loading={loading} tone="secondary" disabled={isPaused}>Generate profile now</Button></div>
        {workflow.notice ? <p className={styles.successMessage} role="status" aria-live="polite">{workflow.notice}</p> : null}
      </div>
    </Panel>
  );
}
