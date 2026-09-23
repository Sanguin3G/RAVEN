import type { DossierProfile } from "./dossierTypes";
import type { WorkspaceInvestigation } from "./investigationTypes";
import { investigationPurposeLabel, investigationStatusLabel } from "./investigationTypes";
import workspaceStyles from "./company-workspace.module.css";
import investigationStyles from "./company-investigations.module.css";

const styles = { ...workspaceStyles, ...investigationStyles };
const date = (value: string) => new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short", year: "numeric" }).format(new Date(value));

interface Props {
  companyName: string; profile?: DossierProfile | null; investigation: WorkspaceInvestigation; busy?: boolean;
  onResearchFurther: () => void; onOpenExternalResearch: () => void;
  onImproveProfile?: () => void; onMarkDone?: () => void; onReopen?: () => void;
  onAddToBriefing?: () => void; onAnalyze?: () => void;
  usedInBriefings?: string[];
}

export function InvestigationWorkspace({ companyName, investigation, busy, onResearchFurther, onOpenExternalResearch,
  onImproveProfile, onMarkDone, onReopen, onAddToBriefing, onAnalyze, usedInBriefings = [] }: Props) {
  const item = investigation;
  const analysis = item.organization;
  const analysisStale = Boolean(analysis && Date.parse(item.materialUpdatedAt) > Date.parse(analysis.createdAt));
  const sourceLeads = item.sourceLeads;
  const profileReview = Boolean(onImproveProfile);
  const primaryAction = profileReview ? onImproveProfile : onAddToBriefing;
  const normalizedTitle = item.title.toLowerCase().replace(/[^a-z0-9]/g, "");
  const normalizedObjective = item.objective.toLowerCase().replace(/[^a-z0-9]/g, "");
  const objectiveIsDistinct = normalizedObjective.length > 0 && !normalizedTitle.includes(normalizedObjective);
  const originMark = item.origin === "Deep Research" ? "✦" : item.origin === "External AI Assist" ? "↗" : "⌕";
  return <article className={styles.investigationWorkspace} aria-labelledby="selected-investigation-heading">
    {item.profileImprovementLocked ? <section className={styles.investigationLockedPanel} aria-label="Profile required"><strong>Profile Improvement is locked.</strong><p>Create a usable accepted Company Profile first. This Investigation remains available as research material and can be used in a Briefing.</p></section> : null}
    <header className={styles.investigationWorkspaceHeader}>
      <div><p className={styles.investigationOrigin}><span aria-hidden="true">{originMark}</span> {item.origin}</p><h2 id="selected-investigation-heading">{item.title}</h2><p className={styles.contextNote}>Updated {date(item.materialUpdatedAt)} · {companyName}</p>
        {objectiveIsDistinct ? <details className={styles.investigationObjective}><summary>Research objective</summary><p>{item.objective}</p></details> : null}
      </div>
    </header>
    <div className={styles.investigationStats} aria-label="Investigation details">
      <span className={styles.investigationPurpose}>{investigationPurposeLabel(item.purpose)}</span>
      <span className={styles.investigationTopicList}>{item.topics.map(topic => <span className={styles.investigationTopic} key={topic}>{topic}</span>)}</span>
      <span className={styles.investigationState}>{item.appliedProfileVersionId ? "Profile improved" : investigationStatusLabel(item.status)}</span>
    </div>
    {primaryAction ? <section className={styles.investigationPrimaryPanel} aria-label="Recommended next action">
      <div><p>{profileReview ? "Ready for profile review" : item.status === "Done" ? "Research ready to reuse" : "Ready for review"}</p>
        <strong>{profileReview ? `Research covers ${item.topics.length || "several"} profile area${item.topics.length === 1 ? "" : "s"}.` : "Turn selected findings into reusable company intelligence."}</strong>
        <span>{profileReview ? "Review and confirm proposed changes before they become accepted Company Profile truth." : "This Investigation can be included in a thematic Briefing without changing its original purpose."}</span>
      </div>
      <button className="button" type="button" disabled={busy} onClick={primaryAction}>{profileReview ? "Improve profile" : "Add to briefing"} <span aria-hidden="true">→</span></button>
    </section> : null}
    <div className={styles.investigationOtherActions} aria-label="Other Investigation actions">
      <div className={styles.investigationLifecycleActions}>
        {onMarkDone ? <button className="button button--quiet" type="button" disabled={busy} onClick={onMarkDone}>✓ Mark as done</button> : null}
        {onReopen ? <button className="button button--quiet" type="button" disabled={busy} onClick={onReopen}>↶ Reopen</button> : null}
        {primaryAction && !profileReview && onAddToBriefing ? <span className={styles.investigationUsageHint}>Use the selected material in a Briefing</span> : null}
      </div>
      <details className={styles.investigationResearchMenu}><summary>Research further</summary><div><button type="button" onClick={onResearchFurther}>Deep Research</button><button type="button" onClick={onOpenExternalResearch}>External AI Assist</button></div></details>
    </div>
    <p className={styles.contextNote}>Research material is not accepted Company Profile truth.</p>
    {usedInBriefings.length ? <p className={styles.contextNote}>Used in briefings: {usedInBriefings.join(", ")}</p> : null}
    {item.status === "Running" ? <p role="status">Research is running. Findings will appear here when ready.</p> : null}
    {item.status === "Failed" ? <p role="status">This research did not complete. The recorded error or material remains available below.</p> : null}
    <div className={styles.investigationEvidenceStrip} aria-label="Research material counts">
      <span><strong>{item.claims.length}</strong> claims</span><span><strong>{sourceLeads.length}</strong> source leads</span><span><strong>{item.uncertainties.length}</strong> uncertainties</span>
    </div>
    <section className={styles.investigationWorkspaceSection}><h3>Executive summary</h3><p className={styles.investigationSummary}>{analysis?.executiveSummary || item.summary}</p></section>
    {onAnalyze ? <section className={styles.investigationOrganizeEmpty}>
      {analysis ? <><h3>{analysisStale ? "New research material is available" : `Analysis updated ${date(analysis.createdAt)}`}</h3><p>{analysisStale ? "Refresh the derived themes and gaps for this Investigation." : "Themes and gaps were derived from this Investigation's saved material."}</p></>
        : <><h3>Research material collected</h3><p>RAVEN can derive themes, gaps, and follow-up questions from this material.</p></>}
      <button className="button button--secondary" type="button" disabled={busy} onClick={onAnalyze}>{busy ? "Analyzing…" : analysis ? "Refresh analysis" : "Analyze findings"}</button>
    </section> : null}
    {analysis ? <>
      <section className={styles.investigationWorkspaceSection}><h3>Themes</h3>{analysis.themes.length ? <div className={styles.investigationThemeGrid}>{analysis.themes.map(theme => <article className={styles.investigationTheme} key={theme.name}><h4>{theme.name}</h4><p>{theme.summary}</p></article>)}</div> : <p>No themes were derived.</p>}</section>
      <section className={styles.investigationWorkspaceSection}><h3>Evidence gaps and follow-up</h3><ul className={styles.plainList}>{analysis.evidenceGaps.map(gap => <li key={gap}>{gap}</li>)}{analysis.suggestedFollowUps.map(question => <li key={question}>{question}</li>)}</ul></section>
    </> : null}
    <details className={styles.investigationCollapsibleSection}><summary>Claims ({item.claims.length})</summary>{item.claims.length ? <div className={styles.investigationClaimList}>{item.claims.map((claim, index) => <article className={styles.investigationClaim} key={`${claim.field}-${index}`}><h4>{claim.field}</h4><p>{claim.statement}</p><small>{claim.supportingSourceLeadIds?.length ? "Source leads available" : "Unverified source lead"}</small></article>)}</div> : <p>No normalized claims are available.</p>}</details>
    <details className={styles.investigationCollapsibleSection}><summary>Contradictions / uncertainties ({item.uncertainties.length})</summary>{item.uncertainties.length ? <ul className={styles.plainList}>{item.uncertainties.map((value, index) => <li key={`${value}-${index}`}>{value}</li>)}</ul> : <p>No uncertainties were recorded.</p>}</details>
    <details className={styles.investigationCollapsibleSection}><summary>Source leads ({sourceLeads.length})</summary><p className={styles.contextNote}>Provider citations are research leads, not accepted evidence.</p>{sourceLeads.length ? <ul className={styles.investigationSourceList}>{sourceLeads.map((source, index) => <li key={`${source.url}-${index}`}><a href={source.url} target="_blank" rel="noreferrer">{source.title || source.url}</a><span>{source.publisher}</span></li>)}</ul> : <p>No source leads were recorded.</p>}</details>
    <details className={styles.investigationRawMaterial}><summary>Raw research material / activity</summary><div><p className={styles.unverifiedBadge}>Preserved original material · not accepted evidence</p><pre>{item.rawResponse || item.rawMaterial || "No raw material was stored for this Investigation."}</pre></div></details>
  </article>;
}
