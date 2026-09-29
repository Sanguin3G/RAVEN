import { Link, useLocation, useNavigate } from "react-router-dom";
import { CheckCircle, CircleNotch, FileArrowUp, LockSimple, MagnifyingGlass, Pause, Play, Sparkle, WarningCircle } from "@phosphor-icons/react";
import { acknowledgeWorkspaceResearch, buildWorkspaceResearchReviewKey } from "../../api/workspace";
import type { ActiveResearchRun } from "../../types/research";
import { canPauseResearchStage, researchProgressLabel } from "../../utils/researchProgress";
import { dismissResearchActivity, type ResearchActivity } from "../../utils/researchActivity";
import type { CurrentResearchSession } from "../../utils/researchSession";

function globalResearchState(item: ActiveResearchRun, session: CurrentResearchSession | null) {
  if (session?.runId === item.run.id && session.paused) return "paused";
  if (item.run.stage === "Failed") return "failed";
  if (canPauseResearchStage(item.run.stage)) return "ready";
  return "active";
}

type Props = {
  activeResearch: ActiveResearchRun[];
  currentResearchSession: CurrentResearchSession | null;
  sharedResearchActivities: ResearchActivity[];
  togglePauseResearch: (runId: string) => void;
  cancelActiveResearch: (runId: string) => Promise<void>;
};

export function ResearchActivityPanel({ activeResearch, currentResearchSession, sharedResearchActivities, togglePauseResearch, cancelActiveResearch }: Props) {
  const navigate = useNavigate();
  const location = useLocation();
  const researchActivityReady = activeResearch.filter((item) => globalResearchState(item, currentResearchSession) === "ready").length
    + sharedResearchActivities.filter((item) => item.status === "ready" && !item.locked).length;
  const researchActivityFailed = activeResearch.filter((item) => globalResearchState(item, currentResearchSession) === "failed").length
    + sharedResearchActivities.filter((item) => item.status === "failed").length;
  const researchActivityRunning = activeResearch.filter((item) => globalResearchState(item, currentResearchSession) === "active").length
    + sharedResearchActivities.filter((item) => item.status === "running").length;
  const researchActivityPaused = activeResearch.filter((item) => globalResearchState(item, currentResearchSession) === "paused").length;
  const researchActivityOverallState = researchActivityFailed > 0
    ? "attention"
    : researchActivityRunning > 0 && researchActivityReady > 0
      ? "progress"
      : researchActivityRunning > 0 || researchActivityPaused > 0
        ? "running"
        : "ready";
  const researchActivityOverallLabel = researchActivityOverallState === "attention"
    ? "Research issues"
    : researchActivityOverallState === "ready"
      ? "Ready for review"
      : researchActivityOverallState === "progress"
        ? "Research activity"
        : "Research running";
  return (
    <>
        {(activeResearch.length > 0 || sharedResearchActivities.length > 0) && <section className={`global-research-strip global-research-strip--${researchActivityOverallState}`} aria-label="Research activity">
          <div className="global-research-strip__lead">
            {sharedResearchActivities.some((item) => item.status === "failed") || activeResearch.some((item) => item.run.stage === "Failed") ? <WarningCircle size={18} weight="fill" aria-hidden="true" /> : sharedResearchActivities.some((item) => item.status === "running") || activeResearch.some((item) => !canPauseResearchStage(item.run.stage)) ? <CircleNotch className="global-research-strip__spinner" size={18} weight="bold" aria-hidden="true" /> : <CheckCircle size={18} weight="fill" aria-hidden="true" />}
            <span><strong>{researchActivityOverallLabel}</strong><small className="global-research-strip__counts"><b>{activeResearch.filter((item) => !canPauseResearchStage(item.run.stage)).length + sharedResearchActivities.filter((item) => item.status === "running").length}</b> running <i aria-hidden="true">·</i> <b>{activeResearch.filter((item) => canPauseResearchStage(item.run.stage)).length + sharedResearchActivities.filter((item) => item.status === "ready" && !item.locked).length}</b> ready <i aria-hidden="true">·</i> <b>{researchActivityFailed}</b> issue{researchActivityFailed === 1 ? "" : "s"}</small></span>
          </div>
          <div className="global-research-strip__items">
            {activeResearch.slice(0, 3).map((item) => {
              const { run, companyName } = item;
              const paused = currentResearchSession?.runId === run.id && currentResearchSession.paused;
              const state = globalResearchState(item, currentResearchSession);
              return <div className={`global-research-run global-research-run--${state}`} key={run.id}>
                <Link to={`/companies/new?researchRun=${encodeURIComponent(run.id)}`}><MagnifyingGlass size={16} weight="bold" aria-hidden="true" /><span><small className="global-research-run__origin">RAVEN Research</small><strong>{companyName}</strong><small>{researchProgressLabel(run, paused)}</small></span></Link>
                {canPauseResearchStage(run.stage) ? <button type="button" className="global-research-run__pause" onClick={() => togglePauseResearch(run.id)}>{paused ? <><Play size={13} weight="fill" aria-hidden="true" /> Resume</> : <><Pause size={13} weight="fill" aria-hidden="true" /> Pause</>}</button> : null}
                {run.stage !== "Completed" ? <button type="button" className="global-research-run__cancel" onClick={() => void cancelActiveResearch(run.id)}>Cancel</button> : null}
              </div>;
            })}
            {sharedResearchActivities.slice(0, 6).map((activity) => {
              const Icon = activity.origin === "Deep" ? Sparkle : FileArrowUp;
              const locked = activity.status === "ready" && activity.locked;
              const originLabel = activity.origin === "Deep" ? "Deep Research" : activity.origin === "Briefing" ? "Briefing" : activity.origin === "Native" ? "RAVEN Research" : "External AI Assist";
              const content = <><Icon size={16} weight={activity.origin === "Deep" ? "fill" : "bold"} aria-hidden="true" /><span><small className="global-research-run__origin">{originLabel}</small><strong>{activity.companyName}</strong><small>{locked ? "Create profile to unlock review" : activity.status === "ready" ? "Ready for review" : activity.detail}</small>{activity.status === "ready" && !locked ? <em className="global-research-run__cta">Review result</em> : null}</span></>;
              const accessibleName = `${originLabel} · ${activity.companyName} · ${activity.status === "ready" ? "Review result" : activity.detail}`;
              const openActivity = () => {
                if (locked) return;
                if (activity.status === "ready") {
                  dismissResearchActivity(activity.id);
                  if (activity.origin === "Deep" || activity.origin === "External") {
                    void acknowledgeWorkspaceResearch([{
                      reviewKey: buildWorkspaceResearchReviewKey(activity.origin === "Deep" ? "Deep Research" : "External AI Assist", activity.companyId, activity.objective),
                      acknowledgedThrough: activity.updatedAt,
                    }]);
                  }
                }
                const destinationPath = activity.href?.split("?")[0];
                if (activity.status === "ready" && activity.href) {
                  navigate(activity.href);
                } else if (activity.onOpen && (!destinationPath || destinationPath === location.pathname)) {
                  activity.onOpen();
                } else if (activity.href) {
                  navigate(activity.href);
                } else {
                  activity.onOpen?.();
                }
              };
              return <div className={`global-research-run global-research-run--${locked ? "locked" : activity.status === "ready" ? "ready" : activity.status === "failed" ? "failed" : "active"}`} key={activity.id}>{locked ? <div className="global-research-run__locked" role="status" aria-label={accessibleName}><LockSimple size={16} weight="bold" aria-hidden="true" />{content}</div> : activity.onOpen ? <button type="button" aria-label={accessibleName} className="global-research-run__open" onClick={openActivity}>{content}</button> : <Link aria-label={accessibleName} to={activity.href || `/companies/${encodeURIComponent(activity.companyId)}?tab=investigations`} onClick={openActivity}>{content}</Link>}</div>;
            })}
          </div>
        </section>}
    </>
  );
}
