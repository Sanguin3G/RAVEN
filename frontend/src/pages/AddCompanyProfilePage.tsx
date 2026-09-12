import { Link } from "react-router-dom";
import { Pause, Play, StopCircle } from "@phosphor-icons/react";
import { Button } from "../components/Button";
import { Panel } from "../components/Panel";
import { ResearchActivity, type ResearchStage as ActivityStage } from "../components/sources";
import { ResearchExecutionDetails } from "../components/sources/ResearchExecutionDetails";
import styles from "./research-workspace.module.css";
import {
  CandidateReviewStage,
  CompletionStage,
  EvidenceReviewStage,
  FailureStage,
  GeneratingProfileStage,
  IdentityResolutionStage,
  IdentityStage,
  MatchStage,
  ProfileReviewStage,
} from "./research-workflow/ResearchWorkflowStages";
import { useCompanyResearchWorkflow } from "./research-workflow/useCompanyResearchWorkflow";
import type { WorkspaceView } from "./research-workflow/types";

function activityStage(view: WorkspaceView): ActivityStage {
  switch (view) {
    case "discovering":
      return "discovering";
    case "resolvingIdentity":
      return "resolvingIdentity";
    case "reviewingSources":
      return "awaitingSourceSelection";
    case "acquiring":
      return "acquiring";
    case "reviewingEvidence":
      return "evidenceReady";
    case "generatingProfile":
      return "generatingProfile";
    case "reviewingProfile":
      return "awaitingProfileConfirmation";
    case "completed":
      return "completed";
    case "cancelled":
      return "cancelled";
    case "failed":
      return "failed";
    case "identify":
    case "matching":
      return "identifying";
  }
}

export function AddCompanyProfilePage() {
  const workflow = useCompanyResearchWorkflow();
  const { view, company, run, isPaused, loading, canPause, error, identityCandidates, matches, activityCounters, researchStatusDetail } = workflow;

  return (
    <div className={`page-stack add-profile-page ${styles.page}`}>
      <div className="page-title-row">
        <div>
          <p className="eyebrow">PUBLIC-SOURCE INTELLIGENCE</p>
          <h1 ref={workflow.identityHeadingRef} tabIndex={-1}>Research a company</h1>
          <p className="page-intro">Build a defensible company dossier from public evidence. RAVEN separates identity hints, source review, and acquired evidence.</p>
        </div>
      </div>

      <div className={styles.workspace}>
        <main className={styles.primaryColumn}>
          <IdentityStage workflow={workflow} />
          <MatchStage workflow={workflow} />
          <IdentityResolutionStage workflow={workflow} />
          <CandidateReviewStage workflow={workflow} />
          <EvidenceReviewStage workflow={workflow} />
          <GeneratingProfileStage workflow={workflow} />
          <ProfileReviewStage workflow={workflow} />
          <CompletionStage workflow={workflow} />
          <FailureStage workflow={workflow} />
        </main>
        <aside className={styles.contextColumn}>
          <ResearchActivity
            stage={activityStage(view)}
            items={view === "resolvingIdentity"
              ? [
                  { id: "grounding-complete", label: "Ground company identity", status: "completed", detail: `RAVEN found ${identityCandidates.length} possible research targets.` },
                  { id: "identity-review", label: "Choose the research target", status: "waiting", detail: "Select the intended organization to rebuild targeted source queries." },
                ]
              : view === "matching" && matches.length > 0
                ? [{ id: "duplicate-review", label: "Review existing company match", status: "waiting", detail: "Choose an existing record or create a separate company." }]
                : undefined}
            counters={activityCounters}
            failureMessage={view === "failed" ? error : undefined}
            statusDetail={run ? researchStatusDetail : undefined}
          />
          {run && !["identify", "matching", "completed", "cancelled"].includes(view) ? <div className={styles.researchControls} data-paused={isPaused ? "true" : undefined}>
            <div className={styles.researchControlsCopy}>
              <strong>{isPaused ? "Research paused" : "Research controls"}</strong>
              <span>{canPause ? "Pause at this checkpoint and resume later in this browser tab." : "RAVEN is in a live provider operation. Pause becomes available when this step is safe."}</span>
            </div>
            <div className={styles.researchControlsActions}>
              <Button type="button" tone="secondary" onClick={workflow.togglePause} disabled={loading || (!canPause && !isPaused)}>{isPaused ? <><Play size={16} weight="fill" /> Resume research</> : <><Pause size={16} weight="fill" /> Pause research</>}</Button>
              <Button type="button" tone="quiet" onClick={() => void workflow.cancelCurrentResearch()} disabled={loading}><StopCircle size={16} weight="bold" /> Cancel research</Button>
            </div>
          </div> : null}
          {run ? <ResearchExecutionDetails researchRunId={run.id} /> : null}
          {company ? <div className={styles.compactContext} aria-label="Company context"><span className={styles.compactContextIcon} aria-hidden="true">{company.name.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]).join("").toUpperCase()}</span><span className={styles.compactContextCopy}><small>Research target</small><strong>{company.name}</strong><span>{[company.country, company.legalName || company.website].filter(Boolean).join(" · ") || "Identity details are still being verified"}</span></span></div> : <Panel title="What RAVEN will do" eyebrow="RESEARCH WORKFLOW" className={styles.contextPanel}><ol className={styles.workflowList}><li><strong>Identify</strong><span>Capture a stable company identity and optional hints.</span></li><li><strong>Discover</strong><span>Find and classify public source candidates.</span></li><li><strong>Acquire</strong><span>Let you choose which pages become evidence.</span></li><li><strong>Profile</strong><span>Generate only from acquired, traceable evidence.</span></li></ol></Panel>}
        </aside>
      </div>
    </div>
  );
}
