import { ArrowSquareOut, Sparkle } from "@phosphor-icons/react";
import type { InvestigationOrganization } from "../../api/investigations";
import type { DossierProfile } from "./dossierTypes";
import styles from "./dossier.module.css";
import type { WorkspaceInvestigation } from "./investigationTypes";

export interface InvestigationWorkspaceProps {
  companyName: string;
  profile?: DossierProfile | null;
  investigation: WorkspaceInvestigation;
  onResearchFurther: () => void;
  onOpenExternalResearch: () => void;
  onImproveProfile?: () => void;
  profileImproved?: boolean;
}

function formatDate(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "Date unavailable" : new Intl.DateTimeFormat(undefined, { day: "numeric", month: "short", year: "numeric", hour: "numeric", minute: "2-digit" }).format(date);
}

function OrganizationSections({ organization }: { organization: InvestigationOrganization }) {
  return <>
    <section className={styles.investigationWorkspaceSection} aria-labelledby="investigation-themes-heading"><div className={styles.investigationSectionHeader}><h3 id="investigation-themes-heading">Organized themes</h3><span>Organization v{organization.version}</span></div>{organization.themes.length ? <div className={styles.investigationThemeGrid}>{organization.themes.map((theme) => <article className={styles.investigationTheme} key={theme.name}><h4>{theme.name}</h4><p>{theme.summary || "No summary was derived."}</p><small>{theme.claimCount} claim{theme.claimCount === 1 ? "" : "s"}</small></article>)}</div> : <p className={styles.contextNote}>No themes were detected.</p>}</section>
    <section className={styles.investigationWorkspaceSection} aria-labelledby="investigation-gaps-heading"><div className={styles.investigationSectionHeader}><h3 id="investigation-gaps-heading">Evidence gaps / follow-up</h3></div>{organization.evidenceGaps.length || organization.suggestedFollowUps.length ? <div className={styles.investigationGapGrid}>{organization.evidenceGaps.length ? <div><h4>Gaps</h4><ul className={styles.plainList}>{organization.evidenceGaps.map((gap) => <li key={gap}>{gap}</li>)}</ul></div> : null}{organization.suggestedFollowUps.length ? <div><h4>Suggested follow-up</h4><ul className={styles.plainList}>{organization.suggestedFollowUps.map((followUp) => <li key={followUp}>{followUp}</li>)}</ul></div> : null}</div> : <p className={styles.contextNote}>No follow-up gaps were derived.</p>}</section>
  </>;
}

function profileHasField(profile: DossierProfile | null | undefined, field: string) {
  if (!profile) return false;
  const normalized = field.toLowerCase();
  if (normalized.includes("lead") || normalized.includes("executive")) return Boolean(profile.leadership?.length);
  if (normalized.includes("employee") || normalized.includes("scale") || normalized.includes("workforce")) return Boolean(profile.companySize || profile.employeeCount || profile.employeeCountRange);
  if (normalized.includes("market") || normalized.includes("customer")) return Boolean(profile.markets?.length);
  if (normalized.includes("location") || normalized.includes("office") || normalized.includes("headquarter")) return Boolean(profile.locations?.length || profile.headquarters);
  if (normalized.includes("product") || normalized.includes("service")) return Boolean(profile.productsServices?.length);
  if (normalized.includes("industry")) return Boolean(profile.primaryIndustry);
  if (normalized.includes("founded") || normalized.includes("history")) return Boolean(profile.foundedYear);
  if (normalized.includes("legal") || normalized.includes("identity")) return Boolean(profile.legalName || profile.registrationNumberOrTaxId);
  return false;
}

function ReviewSummary({ investigation, profile }: { investigation: WorkspaceInvestigation; profile?: DossierProfile | null }) {
  const sourced = investigation.claims.filter((claim) => claim.supportingSourceLeadIds?.length).length;
  const needsSources = investigation.claims.length - sourced;
  const overlapsProfile = investigation.claims.filter((claim) => profileHasField(profile, claim.field)).length;
  return <section className={styles.investigationReview} aria-labelledby="investigation-review-heading"><div className={styles.investigationSectionHeader}><div><h3 id="investigation-review-heading">RAVEN review</h3><p>Claims are compared with the accepted profile before any proposal is prepared. No second source crawl is started.</p></div><span>Reviewable material</span></div><div className={styles.investigationReviewStats}><span><strong>{sourced}</strong> with citations</span><span><strong>{needsSources}</strong> need citation context</span><span><strong>{overlapsProfile}</strong> overlap accepted fields</span></div><details><summary>See comprehensive review details</summary><ul className={styles.plainList}><li>Original provider material, claims, URLs, and uncertainties stay preserved.</li><li>The review distinguishes new profile gaps from information that overlaps the accepted profile.</li>{needsSources ? <li>{needsSources} claim{needsSources === 1 ? "" : "s"} need additional human scrutiny because the material has no attached citation.</li> : null}<li>Any proposed profile change remains a separate user-confirmed step.</li></ul></details></section>;
}

function SectionSummary({ title, count }: { title: string; count?: number }) {
  return <div className={styles.investigationSectionHeader}><h3>{title}</h3>{count === undefined ? null : <span>{count}</span>}</div>;
}

export function InvestigationWorkspace({ companyName, profile, investigation, onResearchFurther, onOpenExternalResearch, onImproveProfile, profileImproved = false }: InvestigationWorkspaceProps) {
  const claims = investigation.claims;
  const sources = investigation.sourceLeads;
  const uncertainties = investigation.uncertainties;
  const locked = investigation.locked === true;
  return <article className={styles.investigationWorkspace} aria-labelledby="selected-investigation-heading">
    {locked ? <section className={styles.investigationLockedPanel} aria-label="Profile required"><strong>This Deep Research result is preserved, but cannot be applied yet.</strong><p>Finish creating the initial company profile. After Profile v1 exists, this result becomes reviewable and can propose a new profile version.</p></section> : null}
    <header className={styles.investigationWorkspaceHeader}><div><p className={styles.eyebrow}>INVESTIGATION · {investigation.origin}</p><h2 id="selected-investigation-heading">{investigation.title}</h2><p className={styles.investigationObjective}>{investigation.objective}</p><p className={styles.contextNote}>Updated {formatDate(investigation.updatedAt)} · {companyName}</p></div><div className={styles.investigationWorkspaceActions}><button className={`${styles.investigationAction} ${styles.investigationActionPrimary}`} type="button" onClick={onResearchFurther}><Sparkle size={16} weight="fill" aria-hidden="true" /><span><strong>Deep Research</strong><small>Continue with a broader investigation</small></span></button><button className={`${styles.investigationAction} ${styles.investigationActionExternal}`} type="button" onClick={onOpenExternalResearch}><ArrowSquareOut size={16} weight="bold" aria-hidden="true" /><span><strong>External AI Assist</strong><small>Use another web-enabled assistant</small></span></button></div></header>
    <div className={styles.investigationStats} aria-label="Investigation summary"><span className={styles.investigationCategory}>{investigation.category}</span><span>{sources.length} source lead{sources.length === 1 ? "" : "s"}</span><span>{claims.length} claim{claims.length === 1 ? "" : "s"}</span><span>{uncertainties.length} uncertaint{uncertainties.length === 1 ? "y" : "ies"}</span><span className={investigation.status === "Running" ? styles.investigationStatusRunning : styles.investigationStatusReady}>{profileImproved ? "Profile improved" : investigation.status}</span></div>
    {investigation.status === "Ready for review" ? <><ReviewSummary investigation={investigation} profile={profile} />{investigation.category === "Profile improvement" ? <section className={`${styles.investigationNextStep} ${profileImproved ? styles.investigationNextStepCompleted : ""}`} aria-label="Investigation next steps"><div><strong>{profileImproved ? "Profile improvement applied" : "Ready to propose a profile improvement?"}</strong><p>{profileImproved ? "A new immutable profile version was created from this investigation. The original material remains available for review." : "Review the findings first. Improve profile uses this material for a confirmation workflow; it does not start a new search."}</p></div><div className={styles.investigationNextStepActions}><a className="button button--quiet" href="#investigation-review-heading">Review result</a>{onImproveProfile && !profileImproved ? <button className="button button--secondary" type="button" onClick={onImproveProfile}>Improve profile</button> : null}</div></section> : <section className={styles.investigationContextNote}><strong>{investigation.category}</strong><p>This investigation is kept as research material and is not presented as a profile improvement candidate.</p></section>}</> : null}

    <section className={styles.investigationWorkspaceSection} aria-labelledby="investigation-summary-heading"><SectionSummary title="Executive summary" /><p className={styles.investigationSummary}>{investigation.organization?.executiveSummary || investigation.summary || "Research material has been collected but no summary is available."}</p></section>
    {investigation.organization ? <OrganizationSections organization={investigation.organization} /> : <section className={styles.investigationOrganizeEmpty}><h3>Research material collected</h3><p>Use <strong>Organize investigations</strong> at the top of this page to derive themes, findings, and evidence gaps. Raw material remains preserved.</p></section>}

    <details className={styles.investigationCollapsibleSection}><summary><SectionSummary title="Claims" count={claims.length} /></summary>{claims.length ? <div className={styles.investigationClaimList}>{claims.map((claim, index) => <article className={styles.investigationClaim} key={`${claim.field}-${index}`}><div><h4>{claim.field}</h4><p>“{claim.statement}”</p></div><div className={styles.investigationClaimMeta}><span>{claim.supportingSourceLeadIds?.length ? "Source leads available" : "Unverified source lead"}</span><small>Origin · {investigation.origin}</small></div></article>)}</div> : <p className={styles.contextNote}>No normalized claims are available yet.</p>}</details>
    <details className={styles.investigationCollapsibleSection}><summary><SectionSummary title="Contradictions / uncertainties" count={uncertainties.length} /></summary>{uncertainties.length ? <ul className={styles.plainList}>{uncertainties.map((uncertainty) => <li key={uncertainty}>{uncertainty}</li>)}</ul> : <p className={styles.contextNote}>No uncertainties were recorded.</p>}</details>
    <details className={styles.investigationCollapsibleSection}><summary><div className={styles.investigationSectionHeader}><div><h3>Source leads</h3><p className={styles.contextNote}>Citations supplied by the research origin; RAVEN does not re-crawl them.</p></div><span>{sources.length}</span></div></summary>{sources.length ? <ul className={styles.investigationSourceList}>{sources.map((source) => <li key={source.id}><a href={source.url} target="_blank" rel="noreferrer">{source.title || source.url}</a><span>{source.publisher || source.url}</span></li>)}</ul> : <p className={styles.contextNote}>No source leads were detected. Claims remain reviewable research material.</p>}</details>
    <details className={styles.investigationRawMaterial}><summary>Raw research material / activity</summary><div><p className={styles.unverifiedBadge}>Preserved original material · not accepted evidence</p><pre>{investigation.rawResponse || investigation.rawMaterial || "No raw material was stored for this investigation."}</pre></div></details>
  </article>;
}
