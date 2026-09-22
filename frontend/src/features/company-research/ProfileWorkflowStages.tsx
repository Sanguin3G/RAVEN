import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "../../components/Button";
import { Panel } from "../../components/Panel";
import type { CompanyResearchWorkflow } from "./types";
import styles from "./research-workspace.module.css";
import { getManagedResearchJobs } from "../../api/managedResearch";

export function ProfileReviewStage({ workflow }: { workflow: CompanyResearchWorkflow }) {
  const { view, profileCandidate, profileWarnings, loading } = workflow;
  const [overlapWarning, setOverlapWarning] = useState(false);
  const [overlapAcknowledged, setOverlapAcknowledged] = useState(false);
  const [matchingResearch, setMatchingResearch] = useState<string[]>([]);
  useEffect(() => {
    if (view !== "reviewingProfile" || !workflow.company) {
      setMatchingResearch([]);
      setOverlapWarning(false);
      setOverlapAcknowledged(false);
      return;
    }
    let active = true;
    void getManagedResearchJobs(workflow.company.id).then((jobs) => {
      if (!active) return;
      const tokens = ["leadership", "employee", "scale", "market", "location", "product", "service", "industry", "founded", "identity", "registration"];
      setMatchingResearch(jobs.filter((job) => job.purpose === "ProfileImprovement" && (job.status === "Queued" || job.status === "Researching"))
        .filter((job) => tokens.some((token) => job.objective.toLowerCase().includes(token)))
        .map((job) => job.objective));
    }).catch(() => undefined);
    return () => { active = false; };
  }, [view, workflow.company]);
  if (view !== "reviewingProfile" || !profileCandidate) return null;

  return (
    <Panel title="Company Profile preview" eyebrow="STEP 05 · REVIEW PROFILE" className={styles.evidencePanel}>
      <p className="page-intro">This is a generated candidate, not accepted company truth. Confirm only after reviewing its evidence.</p>
      <dl className="definition-list"><div><dt>Summary</dt><dd>{profileCandidate.summary || "Not verified"}</dd></div><div><dt>Industry</dt><dd>{profileCandidate.primaryIndustry || "Not verified"}</dd></div><div><dt>Scale</dt><dd>{profileCandidate.employeeCountRange || profileCandidate.companySize || "Not verified"}</dd></div><div><dt>Evidence groups</dt><dd>{profileCandidate.evidence.length}</dd></div></dl>
      {profileCandidate.productsServices.length ? <section><h3>Products &amp; services</h3><ul>{profileCandidate.productsServices.map((item) => <li key={`${item.name}-${item.type}`}>{item.name}{item.description ? ` — ${item.description}` : ""}</li>)}</ul></section> : null}
      {profileWarnings.length ? <div className={styles.acquisitionIssues} role="status"><h3>Validation notes</h3><ul>{profileWarnings.map((warning) => <li key={warning}>{warning}</li>)}</ul></div> : null}
      {matchingResearch.length > 0 && overlapWarning ? <div className={styles.acquisitionIssues} role="alert"><h3>Research for this profile area is still running.</h3><p>This candidate may overlap with an active investigation. You can wait for new findings or finalize this stable snapshot now.</p><small>{matchingResearch[0]}</small><div className="form-actions"><Button type="button" tone="secondary" onClick={() => { setOverlapWarning(false); setOverlapAcknowledged(true); }}>Wait for research</Button><Button type="button" onClick={() => { setOverlapAcknowledged(true); void workflow.handleConfirmProfile(); }} loading={loading}>Finalize anyway</Button></div></div> : null}
      <div className="form-actions"><Button type="button" onClick={() => matchingResearch.length > 0 && !overlapAcknowledged ? setOverlapWarning(true) : void workflow.handleConfirmProfile()} loading={loading} disabled={overlapWarning}>Confirm Profile</Button><Button type="button" tone="secondary" onClick={() => workflow.setView("reviewingEvidence")}>Back to Evidence</Button></div>
    </Panel>
  );
}

export function GeneratingProfileStage({ workflow }: { workflow: CompanyResearchWorkflow }) {
  if (workflow.view !== "generatingProfile") return null;
  return <Panel title="Building Company Profile" eyebrow="STEP 05 · GEMINI"><p aria-live="polite">Gemini is normalizing only acquired evidence. RAVEN will show a candidate for confirmation when it returns.</p></Panel>;
}

export function CompletionStage({ workflow }: { workflow: CompanyResearchWorkflow }) {
  if (workflow.view !== "completed" || !workflow.company) return null;
  return <Panel title="Company Profile confirmed" eyebrow="RESEARCH COMPLETE"><p>Your accepted dossier is now versioned and traceable to selected evidence.</p><Link className="button" to={`/companies/${workflow.company.id}`}>Open Company workspace</Link></Panel>;
}

export function FailureStage({ workflow }: { workflow: CompanyResearchWorkflow }) {
  if (workflow.view !== "failed" || (!workflow.error && !workflow.restoreError)) return null;
  if (workflow.restoreError) {
    return <Panel title="Saved research state needs to be restored" eyebrow="RESEARCH STATE NOT RESTORED" className={styles.failurePanel}>
      <p className="form-error" role="alert">{workflow.restoreError}</p>
      <div className="form-actions">
        <Button type="button" onClick={() => void workflow.retryRestore()} disabled={workflow.loading}>Retry restore</Button>
        <Button type="button" tone="secondary" onClick={workflow.resetAfterFailure}>Start over</Button>
      </div>
    </Panel>;
  }
  return <Panel title="Research issue" eyebrow="RESEARCH FAILED" className={styles.failurePanel}><p className="form-error" role="alert">{workflow.error}</p><Button type="button" tone="secondary" onClick={workflow.resetAfterFailure}>Start over</Button></Panel>;
}
