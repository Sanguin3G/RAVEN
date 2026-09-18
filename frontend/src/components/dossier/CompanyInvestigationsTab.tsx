import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { Sparkle } from "@phosphor-icons/react";
import { getApiErrorMessage } from "../../api/client";
import { getSavedInvestigations, getInvestigationOrganization, organizeInvestigation, type ResearchClaim, type ResearchSourceLead, type SavedResearchArtifact } from "../../api/investigations";
import { getManagedResearchJobs, type ManagedResearchJob } from "../../api/managedResearch";
import type { ResearchTarget } from "../../api/coverage";
import type { DossierInvestigations, DossierProfile } from "./dossierTypes";
import { InvestigationWorkspace } from "./InvestigationWorkspace";
import type { InvestigationCategory, WorkspaceInvestigation } from "./investigationTypes";
import { artifactToWorkspace, classifyInvestigation } from "./investigationTypes";
import { dismissResearchActivity, hasResearchActivity, upsertResearchActivity } from "../../utils/researchActivity";
import { acknowledgeResearchReview } from "../../utils/researchReviewState";
import { hasUsableAcceptedProfile } from "../../utils/profileReadiness";
import styles from "./dossier.module.css";

export interface CompanyInvestigationsTabProps {
  companyId: string;
  companyName?: string;
  profile?: DossierProfile | null;
  investigations?: DossierInvestigations | null;
  onOpenExternalResearch?: (objective: string) => void;
  onOpenDeepResearch?: (objective?: string) => void;
  onImproveProfile?: (targets: ResearchTarget[], sourceMaterialId?: string, sourceMaterialKind?: "saved" | "managed") => void;
  profileImprovedMaterialIds?: ReadonlySet<string>;
}

function inferProfileTargets(investigation: WorkspaceInvestigation): ResearchTarget[] {
  const text = investigation.claims.map((claim) => claim.field).join(" ").toLowerCase();
  const matches: Array<[ResearchTarget, string[]]> = [
    ["Leadership", ["leadership", "leader", "executive", "ceo", "director"]],
    ["EmployeeScale", ["employee", "headcount", "workforce", "company scale", "scale"]],
    ["Markets", ["market", "expansion", "customer", "geograph"]],
    ["Locations", ["location", "office", "headquarter", "footprint"]],
    ["ProductsServices", ["product", "service", "industry"]],
    ["FoundedHistory", ["founded", "history", "established"]],
    ["LegalIdentity", ["legal", "identity", "registration", "tax"]],
  ];
  const targets = matches.filter(([, terms]) => terms.some((term) => text.includes(term))).map(([target]) => target);
  return targets.length ? targets : ["Markets"];
}

function syncManagedResearchActivity(companyName: string, job: ManagedResearchJob, hasAcceptedProfile: boolean) {
  const activityId = `deep-${job.id}`;
  if ((job.status === "Completed" || job.status === "Failed" || job.status === "Cancelled") && !hasResearchActivity(activityId)) return;
  const status = job.status === "Completed" ? "ready" : job.status === "Failed" || job.status === "Cancelled" ? "failed" : "running";
  const locked = job.status === "Completed" && job.purpose === "ProfileImprovement" && !hasAcceptedProfile;
  const detail = locked ? "Profile required · create the company profile to unlock review" : job.status === "Queued" ? "Starting investigation" : job.status === "Researching" ? "Researching across sources" : job.status === "Completed" ? "Ready for review" : "Deep Research could not complete";
  upsertResearchActivity({
    id: activityId,
    jobId: job.id,
    origin: "Deep",
    companyId: job.companyId,
    companyName,
    objective: job.objective,
    detail,
    status,
    locked,
    href: `/companies/${encodeURIComponent(job.companyId)}?tab=investigations&research=${encodeURIComponent(job.investigationId ?? job.id)}`,
    updatedAt: job.completedAt || job.createdAt,
  });
}

function formatManagedJob(job: ManagedResearchJob, hasAcceptedProfile: boolean): WorkspaceInvestigation {
  const result = (job.result && typeof job.result === "object" ? job.result : {}) as { summary?: string; claims?: Array<{ topic?: string; field?: string; statement?: string; supportingSourceUrls?: string[] }>; sources?: Array<{ title?: string; url?: string; publisher?: string }>; uncertainties?: string[] };
  const sourceLeads: ResearchSourceLead[] = (result.sources ?? []).filter((source) => source.url).map((source, index) => ({ id: `${job.id}-source-${index}`, url: source.url!, title: source.title, publisher: source.publisher }));
  const sourceIdsByUrl = new Map(sourceLeads.map((source) => [source.url, source.id]));
  const claims: ResearchClaim[] = (result.claims ?? []).filter((claim) => claim.statement).map((claim) => ({ field: claim.topic || claim.field || "Research", statement: claim.statement!, supportingSourceLeadIds: (claim.supportingSourceUrls ?? []).map((url) => sourceIdsByUrl.get(url)).filter((id): id is string => Boolean(id)) }));
  return {
    id: job.investigationId ?? job.id,
    materialId: job.investigationId ?? undefined,
    materialKind: "managed",
    title: job.objective,
    objective: job.objective,
    summary: result.summary || "Managed research is still running or did not return a summary yet.",
    origin: "Deep Research",
    category: classifyInvestigation(job.objective, claims),
    status: job.status === "Completed" ? "Ready for review" : job.status === "Failed" ? "Failed" : "Running",
    updatedAt: job.completedAt || job.createdAt,
    provider: job.provider,
    claims,
    sourceLeads,
    uncertainties: result.uncertainties ?? [],
    rawMaterial: result.summary || job.error || undefined,
    locked: job.status === "Completed" && job.purpose === "ProfileImprovement" && !hasAcceptedProfile,
  };
}

export function CompanyInvestigationsTab({ companyId, companyName = "Company", profile, investigations, onOpenExternalResearch, onOpenDeepResearch, onImproveProfile, profileImprovedMaterialIds }: CompanyInvestigationsTabProps) {
  const [searchParams] = useSearchParams();
  const requestedInvestigationId = searchParams.get("research");
  const [localArtifacts, setLocalArtifacts] = useState<SavedResearchArtifact[] | null>(null);
  const [managedJobs, setManagedJobs] = useState<ManagedResearchJob[]>([]);
  const [loading, setLoading] = useState(!investigations?.artifacts);
  const [error, setError] = useState<string | null>(investigations?.error || null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [categoryFilter, setCategoryFilter] = useState<InvestigationCategory | "All">("All");
  const [organizations, setOrganizations] = useState<Record<string, NonNullable<WorkspaceInvestigation["organization"]>>>({});
  const [organizingAll, setOrganizingAll] = useState(false);
  const [pageOrganizationNotice, setPageOrganizationNotice] = useState<string | null>(null);
  const hasAcceptedProfile = hasUsableAcceptedProfile(profile);

  useEffect(() => {
    if (investigations?.artifacts) return;
    let active = true;
    setLoading(true);
    setError(null);
    void getSavedInvestigations(companyId).then((result) => { if (active) setLocalArtifacts(result); }).catch((reason: unknown) => { if (active) setError(getApiErrorMessage(reason, "Could not load investigations.")); }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [companyId, investigations?.artifacts]);

  useEffect(() => {
    let active = true;
    const loadJobs = () => getManagedResearchJobs(companyId).then((result) => { if (active) setManagedJobs(result); result.forEach((job) => syncManagedResearchActivity(companyName, job, hasAcceptedProfile)); }).catch(() => undefined);
    void loadJobs();
    const interval = window.setInterval(() => { void loadJobs(); }, 8_000);
    return () => { active = false; window.clearInterval(interval); };
  }, [companyId, companyName, hasAcceptedProfile]);

  const artifacts = investigations?.artifacts ?? localArtifacts ?? [];
  const items = useMemo<WorkspaceInvestigation[]>(() => [...managedJobs.map((job) => formatManagedJob(job, hasAcceptedProfile)), ...artifacts.map(artifactToWorkspace)].filter((item, index, all) => all.findIndex((other) => other.id === item.id) === index).sort((left, right) => Date.parse(right.updatedAt) - Date.parse(left.updatedAt)), [artifacts, hasAcceptedProfile, managedJobs]);
  const visibleItems = useMemo(() => categoryFilter === "All" ? items : items.filter((item) => item.category === categoryFilter), [categoryFilter, items]);
  const categoryCounts = useMemo(() => items.reduce<Record<string, number>>((counts, item) => { counts[item.category] = (counts[item.category] || 0) + 1; return counts; }, {}), [items]);

  useEffect(() => {
    if (!visibleItems.length) { setSelectedId(null); return; }
    setSelectedId((current) => {
      if (requestedInvestigationId && visibleItems.some((item) => item.id === requestedInvestigationId)) return requestedInvestigationId;
      return current && visibleItems.some((item) => item.id === current) ? current : visibleItems[0].id;
    });
  }, [requestedInvestigationId, visibleItems]);

  const selected = items.find((item) => item.id === selectedId) ?? null;

  useEffect(() => {
    if (!selected?.artifactId || organizations[selected.artifactId]) return;
    let active = true;
    void getInvestigationOrganization(companyId, selected.artifactId).then((organization) => { if (active) setOrganizations((current) => ({ ...current, [selected.artifactId!]: organization })); }).catch(() => undefined);
    return () => { active = false; };
  }, [companyId, organizations, selected]);

  const selectedWithOrganization = selected && selected.artifactId && organizations[selected.artifactId] ? { ...selected, organization: organizations[selected.artifactId] } : selected;

  useEffect(() => {
    const selectedManagedJob = selected && managedJobs.find((job) => (job.investigationId ?? job.id) === selected.id);
    if (selectedManagedJob?.status === "Completed") {
      dismissResearchActivity(`deep-${selectedManagedJob.id}`);
      if (requestedInvestigationId && selected?.id === requestedInvestigationId) acknowledgeResearchReview(`Deep Research:${selectedManagedJob.id}`);
    }
    if (requestedInvestigationId && selected?.id === requestedInvestigationId && selected.status === "Ready for review") {
      acknowledgeResearchReview(`${selected.origin}:${selected.id}`);
    }
  }, [managedJobs, requestedInvestigationId, selected]);

  const refresh = async () => {
    if (investigations?.onRefresh) { await investigations.onRefresh(); return; }
    setLocalArtifacts(await getSavedInvestigations(companyId));
  };

  const organizeAll = async () => {
    const savedItems = items.filter((item) => item.artifactId);
    if (!savedItems.length) { setPageOrganizationNotice("These results are already grouped by investigation. More organization becomes available after saved research material is added."); return; }
    setOrganizingAll(true);
    setError(null);
    setPageOrganizationNotice(null);
    const results = await Promise.allSettled(savedItems.map((item) => organizeInvestigation(companyId, item.artifactId!)));
    const successful = results.filter((result): result is PromiseFulfilledResult<NonNullable<WorkspaceInvestigation["organization"]>> => result.status === "fulfilled");
    if (successful.length) setOrganizations((current) => ({ ...current, ...Object.fromEntries(successful.map((result) => [result.value.savedResearchArtifactId, result.value])) }));
    const failed = results.length - successful.length;
    setPageOrganizationNotice(failed ? `Organized ${successful.length} investigation${successful.length === 1 ? "" : "s"}; ${failed} could not be refreshed. Original material is unchanged.` : `Organized ${successful.length} investigation${successful.length === 1 ? "" : "s"}. Original material is unchanged.`);
    setOrganizingAll(false);
  };

  return <section className={styles.investigationsPage} data-testid="dossier-investigations" aria-labelledby="dossier-investigations-heading">
    <header className={styles.investigationsPageHeader}><div><p className={styles.eyebrow}>COMPANY · INVESTIGATIONS</p><h2 id="dossier-investigations-heading">Investigations</h2><p className={styles.tabIntro}>Research questions, findings, and evidence leads — organized around the investigation itself.</p></div><span className={styles.contextNote}>Research from RAVEN, Deep Research, and External Assist lands here.</span></header>
    {loading ? <p className={styles.contextNote} role="status">Loading investigations…</p> : null}
    {error ? <p className={styles.errorMessage} role="alert">{error}</p> : null}
    {!loading && !items.length ? <div className={styles.emptyState}><h3>No investigations yet</h3><p>Research a focused question to build a reusable record of findings, sources, and uncertainties.</p>{onOpenDeepResearch ? <button className="button" type="button" onClick={() => onOpenDeepResearch()}>Open Deep Research in Ask RAVEN</button> : <Link className="button" to={`/companies/${encodeURIComponent(companyId)}?tab=investigations&chat=deep`}>Open Deep Research in Ask RAVEN</Link>}</div> : null}
    {!loading && items.length ? <>
      <div className={styles.investigationPageActions}><button className="button button--ai" type="button" onClick={() => void organizeAll()} disabled={organizingAll}><Sparkle size={16} weight="fill" aria-hidden="true" /> {organizingAll ? "Organizing investigations…" : "Organize investigations"}</button><span className={styles.investigationPageHint}>Refresh the organized view across this company’s saved material.</span>{pageOrganizationNotice ? <p className={styles.investigationPageNotice} role="status">{pageOrganizationNotice}</p> : null}</div>
      <div className={styles.investigationWorkspaceLayout}>
        <div className={styles.investigationPicker}><label htmlFor="investigation-picker"><span>Current investigations</span><select id="investigation-picker" value={selectedId ?? ""} onChange={(event) => setSelectedId(event.target.value)}>{visibleItems.map((item) => <option key={item.id} value={item.id}>{item.title} · {item.category}</option>)}</select></label><label htmlFor="investigation-category"><span>View</span><select id="investigation-category" value={categoryFilter} onChange={(event) => setCategoryFilter(event.target.value as InvestigationCategory | "All")}><option value="All">All categories ({items.length})</option><option value="Profile improvement">Profile improvement ({categoryCounts["Profile improvement"] || 0})</option><option value="Financial / performance">Financial / performance ({categoryCounts["Financial / performance"] || 0})</option><option value="Market / strategy">Market / strategy ({categoryCounts["Market / strategy"] || 0})</option><option value="General research">General research ({categoryCounts["General research"] || 0})</option></select></label><small>{visibleItems.length} shown · Select one to review</small></div>
        {!visibleItems.length ? <p className={styles.contextNote} role="status">No investigations match this category.</p> : null}
        {selectedWithOrganization ? <InvestigationWorkspace companyName={companyName} profile={profile} investigation={selectedWithOrganization} onResearchFurther={() => onOpenDeepResearch?.(selectedWithOrganization.objective)} onOpenExternalResearch={() => onOpenExternalResearch?.(selectedWithOrganization.objective)} profileImproved={Boolean(selectedWithOrganization.materialId && profileImprovedMaterialIds?.has(selectedWithOrganization.materialId))} onImproveProfile={onImproveProfile && !selectedWithOrganization.locked && selectedWithOrganization.category === "Profile improvement" ? () => onImproveProfile(inferProfileTargets(selectedWithOrganization), selectedWithOrganization.materialId, selectedWithOrganization.materialKind) : undefined} /> : null}
      </div>
    </> : null}
    {!loading && items.length && investigations?.onRefresh ? <button className="button button--secondary" type="button" onClick={() => void refresh()}>Refresh investigations</button> : null}
  </section>;
}
