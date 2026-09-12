import { useEffect, useMemo, useState } from "react";
import { ArrowRight, CheckCircle, CircleNotch, Pause, Play, Sparkle, StopCircle, WarningCircle } from "@phosphor-icons/react";
import { Link } from "react-router-dom";
import { Panel } from "../components/Panel";
import { getApiErrorMessage } from "../api/client";
import { getCompanies } from "../api/companies";
import { cancelResearchRun, getActiveResearchRuns, getResearchRun, type ActiveResearchRun } from "../api/research";
import { getWorkspaceReview, type WorkspaceReviewResponse } from "../api/workspace";
import type { Company } from "../types/company";
import { canPauseResearchStage, isFinishedResearch, researchProgressLabel } from "../utils/researchProgress";
import { clearCurrentResearch, readCurrentResearch, rememberCurrentResearch, setCurrentResearchPaused, type CurrentResearchSession } from "../utils/researchSession";

function getInitials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]).join("").toUpperCase() || "?";
}

export function DashboardPage() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [review, setReview] = useState<WorkspaceReviewResponse | null>(null);
  const [activeResearch, setActiveResearch] = useState<ActiveResearchRun[]>([]);
  const [currentResearchSession, setCurrentResearchSession] = useState<CurrentResearchSession | null>(readCurrentResearch);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let mounted = true;
    const refreshResearch = async () => {
      const serverRuns = await getActiveResearchRuns().catch(() => null);
      if (!mounted) return;

      let session = readCurrentResearch();
      let rememberedRun: ActiveResearchRun | null = null;
      if (session) {
        const serverRun = serverRuns?.find((item) => item.run.id === session?.runId);
        const run = serverRun?.run ?? await getResearchRun(session.runId).catch(() => null);
        if (run) {
          if (isFinishedResearch(run)) {
            clearCurrentResearch(run.id);
            session = null;
          } else {
            if (!canPauseResearchStage(run.stage) && session.paused) {
              setCurrentResearchPaused(run.id, false);
              session = readCurrentResearch();
            }
            rememberedRun = { run, companyName: session?.companyName ?? "Company research" };
          }
        }
      }
      if (!session && serverRuns && serverRuns.length > 0) {
        rememberCurrentResearch(serverRuns[0].run, serverRuns[0].companyName);
        session = readCurrentResearch();
      }
      const merged = [...(Array.isArray(serverRuns) ? serverRuns : [])];
      if (rememberedRun && !merged.some((item) => item.run.id === rememberedRun?.run.id)) merged.unshift(rememberedRun);
      setCurrentResearchSession(session);
      setActiveResearch(merged);
    };

    Promise.all([getCompanies(), getWorkspaceReview()])
      .then(([companyResult, reviewResult]) => {
        if (!mounted) return;
        setCompanies(companyResult);
        setReview(reviewResult);
      })
      .catch((reason: unknown) => { if (mounted) setError(getApiErrorMessage(reason, "Could not load workspace overview.")); })
      .finally(() => { if (mounted) setLoading(false); });
    void refreshResearch();
    const timer = window.setInterval(() => { void refreshResearch(); }, 2_500);
    return () => { mounted = false; window.clearInterval(timer); };
  }, []);

  const recommendations = review?.recommendations ?? [];
  const duplicateGroups = review?.duplicateGroups ?? [];
  const attention = useMemo(() => recommendations.filter((item) => item.kind !== "PossibleDuplicate").slice(0, 6), [recommendations]);
  const sparseCount = recommendations.filter((item) => item.kind === "SparseProfile").length;
  const duplicateCount = duplicateGroups.length;
  const recentCompanies = [...companies].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)).slice(0, 5);

  async function cancel(run: ActiveResearchRun) {
    await cancelResearchRun(run.run.id).catch(() => undefined);
    clearCurrentResearch(run.run.id);
    setCurrentResearchSession(readCurrentResearch());
    setActiveResearch((current) => current.filter((item) => item.run.id !== run.run.id));
  }

  function togglePause(run: ActiveResearchRun) {
    if (!canPauseResearchStage(run.run.stage)) return;
    const paused = currentResearchSession?.runId === run.run.id && currentResearchSession.paused;
    setCurrentResearchSession(setCurrentResearchPaused(run.run.id, !paused));
  }

  return <div className="page-stack dashboard-page">
    <section className="dashboard-hero" aria-labelledby="dashboard-title">
      <div><p className="eyebrow">RAVEN WORKSPACE</p><h1 id="dashboard-title">Know what needs attention.</h1><p className="dashboard-hero__intro">Track profile health, follow research that is still running, and act on the companies RAVEN has flagged.</p></div>
      <div className="dashboard-hero__actions"><Link className="button" to="/companies/new">+ Research a company</Link><Link className="button button--secondary" to="/companies">Open workspace</Link></div>
    </section>

    {error ? <div className="form-error" role="alert">{error}</div> : null}

    <section className="dashboard-priority" aria-labelledby="dashboard-priority-heading">
      <div className="section-heading"><div><p className="eyebrow">WORKSPACE PRIORITY</p><h2 id="dashboard-priority-heading">What should you do next?</h2></div><Link className="text-link" to="/companies">See all companies <ArrowRight size={15} weight="bold" aria-hidden="true" /></Link></div>
      <div className="stats-grid">
        <div className="stat-card stat-card--attention"><span className="stat-card__label">Needs attention</span><strong>{loading ? "-" : recommendations.length + duplicateCount}</strong><span className="stat-card__detail">Named in workspace review</span></div>
        <div className="stat-card stat-card--running"><span className="stat-card__label">Research running</span><strong>{loading ? "-" : activeResearch.length}</strong><span className="stat-card__detail">Persists while you navigate</span></div>
        <div className="stat-card"><span className="stat-card__label">Companies</span><strong>{loading ? "-" : companies.length}</strong><span className="stat-card__detail">{sparseCount} sparse | {duplicateCount} duplicate group{duplicateCount === 1 ? "" : "s"}</span></div>
      </div>
    </section>

    <div className="dashboard-columns dashboard-columns--priority">
      <Panel title="Companies needing attention" eyebrow="REVIEW QUEUE" className="dashboard-attention-panel">
        {attention.length === 0 && !loading ? <div className="empty-state"><strong>No recommendation queue.</strong><p>Open Company List to run a fresh workspace review.</p></div> : null}
        {loading ? <div className="empty-state"><strong>Loading workspace review…</strong><p>Checking health, duplicates, and recency.</p></div> : null}
        {attention.length > 0 ? <div className="dashboard-attention-list">{attention.map((item) => <Link className="dashboard-attention" key={`${item.kind}-${item.companyId}`} to={item.kind === "SparseProfile" || item.kind === "Stale" ? `/companies/new?refreshCompanyId=${encodeURIComponent(item.companyId)}` : `/companies/${encodeURIComponent(item.companyId)}?review=true`}><span className="dashboard-attention__icon"><WarningCircle size={18} weight="fill" aria-hidden="true" /></span><span><strong>{companies.find((company) => company.id === item.companyId)?.name ?? "Company record"}</strong><small>{item.title}</small><em>{item.summary}</em></span><ArrowRight size={16} weight="bold" aria-hidden="true" /></Link>)}</div> : null}
        <Link className="text-link" to="/companies">Open full review queue <ArrowRight size={15} weight="bold" aria-hidden="true" /></Link>
      </Panel>

      <Panel title="Research in progress" eyebrow="BACKGROUND WORK" className="dashboard-research-panel">
        {activeResearch.length === 0 && !loading ? <div className="empty-state"><strong>No active research.</strong><p>Start a refresh or new company research and keep working elsewhere.</p><Link className="button button--secondary" to="/companies/new">Start research</Link></div> : null}
        {loading ? <div className="empty-state"><strong>Checking active runs…</strong></div> : null}
        {activeResearch.length > 0 ? <div className="dashboard-running-list">{activeResearch.map((item) => { const paused = currentResearchSession?.runId === item.run.id && currentResearchSession.paused; const checkpoint = canPauseResearchStage(item.run.stage); return <article className={`dashboard-running${paused ? " dashboard-running--paused" : checkpoint ? " dashboard-running--ready" : ""}`} key={item.run.id}><div className="dashboard-running__title">{paused ? <Pause size={18} weight="fill" aria-hidden="true" /> : checkpoint ? <CheckCircle size={18} weight="fill" aria-hidden="true" /> : <CircleNotch className="dashboard-running__spinner" size={18} weight="bold" aria-hidden="true" />}<div><strong>{item.companyName}</strong><small>{researchProgressLabel(item.run, paused)}</small></div></div><p>{item.run.sourcesFound} search result{item.run.sourcesFound === 1 ? "" : "s"} · {item.run.uniqueCandidates} candidate{item.run.uniqueCandidates === 1 ? "" : "s"}</p><div className="dashboard-running__actions"><Link className="button button--quiet" to={`/companies/new?researchRun=${encodeURIComponent(item.run.id)}`}>Open research</Link>{checkpoint ? <button className="button button--quiet" type="button" onClick={() => togglePause(item)}>{paused ? <><Play size={14} weight="fill" /> Resume</> : <><Pause size={14} weight="fill" /> Pause</>}</button> : null}<button className="button button--quiet" type="button" onClick={() => void cancel(item)}><StopCircle size={14} weight="bold" /> Cancel</button></div></article>; })}</div> : null}
      </Panel>
    </div>

    <Panel title="Recently changed companies" eyebrow="WORKSPACE PULSE" className="dashboard-recent">
      {recentCompanies.length ? <div className="company-preview-list">{recentCompanies.map((company) => <Link className="company-preview" key={company.id} to={`/companies/${company.id}`}><span className="company-avatar" aria-hidden="true">{getInitials(company.name)}</span><span className="company-preview__main"><strong>{company.name}</strong><small>{company.country || "Country not provided"}</small></span><span className="status">Open profile</span></Link>)}</div> : <div className="empty-state"><strong>No companies yet.</strong><p>Research a company to build your workspace.</p></div>}
    </Panel>
  </div>;
}
