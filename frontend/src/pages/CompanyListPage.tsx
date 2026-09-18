import { useEffect, useMemo, useRef, useState, type MouseEvent, type ReactNode } from "react";
import { Archive } from "@phosphor-icons/react/dist/csr/Archive";
import { ArrowUUpLeft } from "@phosphor-icons/react/dist/csr/ArrowUUpLeft";
import { DotsThreeVertical } from "@phosphor-icons/react/dist/csr/DotsThreeVertical";
import { GitMerge } from "@phosphor-icons/react/dist/csr/GitMerge";
import { MagnifyingGlass } from "@phosphor-icons/react/dist/csr/MagnifyingGlass";
import { Sparkle } from "@phosphor-icons/react/dist/csr/Sparkle";
import { Trash } from "@phosphor-icons/react/dist/csr/Trash";
import { CheckCircle } from "@phosphor-icons/react/dist/csr/CheckCircle";
import { WarningCircle } from "@phosphor-icons/react/dist/csr/WarningCircle";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { Panel } from "../components/Panel";
import { getApiErrorMessage } from "../api/client";
import { getCompanies } from "../api/companies";
import {
  archiveCompany,
  confirmCompanyMerge,
  deleteCompany,
  getWorkspaceReview,
  previewCompanyMerge,
  restoreCompany,
  type CompanyDuplicateGroup,
  type CompanyHealthAssessment,
  type CompanyHealthStatus,
  type CompanyMergePreview,
  type WorkspaceResearchReviewItem,
  type WorkspaceReviewRecommendation,
  type WorkspaceReviewResponse,
} from "../api/workspace";
import type { Company } from "../types/company";
import { acknowledgeResearchReview, isResearchReviewAcknowledged } from "../utils/researchReviewState";

function getInitials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]).join("").toUpperCase() || "?";
}

function formatDate(value?: string | null) {
  if (!value) return "Not researched";
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return "Not researched";
  return new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric" }).format(parsed);
}

function statusLabel(status: string) {
  return status.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function statusClass(status: CompanyHealthStatus) {
  return `status status--health-${status.replace(/([a-z])([A-Z])/g, "$1-$2").toLowerCase()}`;
}

function healthFor(company: Company, review: WorkspaceReviewResponse | null): CompanyHealthAssessment | null {
  return (review?.companies ?? []).find((item) => item.companyId === company.id)?.health ?? null;
}

function ResearchReviewGroup({ title, items, icon: Icon, tone, onOpen, showHeading = true }: { title: string; items: WorkspaceResearchReviewItem[]; icon: typeof CheckCircle; tone: "ready" | "issue"; onOpen: (item: WorkspaceResearchReviewItem) => void; showHeading?: boolean }) {
  if (items.length === 0) return null;
  return <section className={`workspace-review-section workspace-review-section--research workspace-review-section--${tone}`}>{showHeading ? <div className="workspace-review-section__heading"><div><h3><Icon size={18} weight="fill" aria-hidden="true" /> {title}</h3><span>{items.length} item{items.length === 1 ? "" : "s"}</span></div></div> : null}<div className="workspace-review-research-list">{items.slice(0, 4).map((item) => <article className="workspace-review-research-item" key={`${item.method}-${item.itemId}`}><div><strong>{item.companyName}</strong><small>{item.method} · {item.title}</small>{item.detail ? <p>{item.detail}</p> : null}</div><button className="button button--quiet" type="button" onClick={() => onOpen(item)}>{tone === "ready" ? "Review" : "Open issue"}</button></article>)}</div>{items.length > 4 ? <p className="workspace-review-more">Showing 4 of {items.length}. Open the company investigation for the full history.</p> : null}</section>;
}

function ReviewMetric({ label, count, detail, tone }: { label: string; count: number; detail: string; tone: "ready" | "attention" | "duplicate" | "issue" }) {
  function focusSection() {
    const sectionLabel = label === "Duplicates" ? "Possible duplicates" : label === "Profile attention" ? "Companies needing attention" : label;
    const section = [...document.querySelectorAll<HTMLElement>("details.workspace-review-category")].find((candidate) => candidate.textContent?.includes(sectionLabel));
    if (!section) return;
    section.setAttribute("open", "");
    section.scrollIntoView({ behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth", block: "start" });
  }

  return <button className={`workspace-review-metric workspace-review-metric--${tone}`} type="button" onClick={focusSection} disabled={count === 0} aria-label={`${label}: ${count}. ${detail}. Click to open this section.`}><strong>{count}</strong><span>{label}</span><small>{detail}</small></button>;
}

function defaultHealth(company: Company): CompanyHealthStatus {
  return company.archivedAt ? "Archived" : company.lastResearchedAt ? "Partial" : "Unresearched";
}

interface WorkspaceDialogProps {
  open: boolean;
  title: string;
  labelledBy: string;
  children: ReactNode;
  onClose: () => void;
}

function WorkspaceDialog({ open, title, labelledBy, children, onClose }: WorkspaceDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) {
      if (typeof dialog.showModal === "function") dialog.showModal();
      else dialog.setAttribute("open", "");
    } else if (!open && dialog.open) {
      if (typeof dialog.close === "function") dialog.close();
      else dialog.removeAttribute("open");
    }
  }, [open]);

  return (
    <dialog ref={dialogRef} className="workspace-dialog" aria-labelledby={labelledBy} onCancel={onClose}>
      {open ? <div className="workspace-dialog__content">
        <header className="workspace-dialog__header"><h2 id={labelledBy}>{title}</h2><button className="modal-close" type="button" aria-label="Close dialog" onClick={onClose}>×</button></header>
        {children}
      </div> : null}
    </dialog>
  );
}

function MergePreviewDialog({ open, preview, loading, error, busy, onClose, onConfirm }: { open: boolean; preview: CompanyMergePreview | null; loading: boolean; error: string | null; busy: boolean; onClose: () => void; onConfirm: () => void }) {
  return <WorkspaceDialog open={open} title="Review company merge" labelledBy="merge-dialog-title" onClose={onClose}>
    {loading ? <p className="state-message">Preparing a safe merge preview…</p> : null}
    {error ? <div className="form-error" role="alert">{error}</div> : null}
    {preview ? <>
      <p className="workspace-dialog__intro">RAVEN will keep the canonical record and move compatible research history to it. Nothing changes until you confirm.</p>
      <div className="merge-comparison">
        <section className="merge-comparison__side"><p className="eyebrow">KEEP</p><h3>{preview.canonicalCompanyName}</h3><p>Canonical company record</p><small>{preview.profileVersions} profile version{preview.profileVersions === 1 ? "" : "s"} · {preview.sourceDocuments} source document{preview.sourceDocuments === 1 ? "" : "s"}</small></section>
        <div className="merge-comparison__arrow" aria-hidden="true">→</div>
        <section className="merge-comparison__side merge-comparison__side--duplicate"><p className="eyebrow">MERGE FROM</p><h3>{preview.duplicateCompanyName}</h3><p>Duplicate record</p><small>{preview.researchRuns} research run{preview.researchRuns === 1 ? "" : "s"} · {preview.savedInvestigations} saved investigation{preview.savedInvestigations === 1 ? "" : "s"}</small></section>
      </div>
      <section className="merge-preservation"><h3>RAVEN will preserve</h3><ul><li>Research history and profile versions</li><li>Unique sources and evidence provenance</li><li>Saved investigations and compatible monitoring state</li></ul>{preview.warnings.map((warning) => <p key={warning} className="merge-warning">{warning}</p>)}</section>
      <div className="modal-actions"><button className="button button--secondary" type="button" onClick={onClose} disabled={busy}>Cancel</button><button className="button" type="button" onClick={onConfirm} disabled={busy}>{busy ? "Merging…" : "Merge companies"}</button></div>
    </> : null}
    {!loading && !preview && !error ? <p className="state-message">No merge preview is available.</p> : null}
  </WorkspaceDialog>;
}

export function CompanyListPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const reviewRequested = searchParams.get("review") === "true";
  const [companies, setCompanies] = useState<Company[]>([]);
  const [review, setReview] = useState<WorkspaceReviewResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [reviewLoading, setReviewLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reviewError, setReviewError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [country, setCountry] = useState("");
  const [healthFilter, setHealthFilter] = useState("");
  const [researchedFilter, setResearchedFilter] = useState("");
  const [showArchived, setShowArchived] = useState(false);
  const [reviewOpen, setReviewOpen] = useState(reviewRequested);
  const [openMenuId, setOpenMenuId] = useState<string | null>(null);
  const [menuPlacement, setMenuPlacement] = useState<"up" | "down">("down");
  const [busyCompanyId, setBusyCompanyId] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<Company | null>(null);
  const [deleteBusy, setDeleteBusy] = useState(false);
  const [mergePair, setMergePair] = useState<{ canonicalId: string; duplicateId: string } | null>(null);
  const [mergePreview, setMergePreview] = useState<CompanyMergePreview | null>(null);
  const [mergeLoading, setMergeLoading] = useState(false);
  const [mergeBusy, setMergeBusy] = useState(false);
  const [mergeError, setMergeError] = useState<string | null>(null);
  const [reviewAcknowledgementVersion, setReviewAcknowledgementVersion] = useState(0);

  async function loadWorkspace(includeArchived = showArchived) {
    setReviewLoading(true);
    setReviewError(null);
    try {
      const workspace = await getWorkspaceReview(includeArchived);
      setReview(Array.isArray(workspace?.companies) ? workspace : null);
    } catch (reason) {
      setReviewError(getApiErrorMessage(reason, "Could not review the workspace."));
    } finally {
      setReviewLoading(false);
    }
  }

  useEffect(() => {
    let active = true;
    Promise.all([getCompanies(), getWorkspaceReview(showArchived)])
      .then(([result, workspace]) => {
        if (!active) return;
        setCompanies(result);
        // Keep the list usable with older fixture adapters that only implement
        // the companies endpoint while the optional health review is unavailable.
        if (Array.isArray(workspace?.companies)) setReview(workspace);
      })
      .catch((reason: unknown) => { if (active) setError(getApiErrorMessage(reason, "Could not load companies.")); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [showArchived]);

  const countries = useMemo(() => [...new Set(companies.map((company) => company.country).filter(Boolean))] as string[], [companies]);
  const filteredCompanies = useMemo(() => companies.filter((company) => {
    const query = search.trim().toLowerCase();
    const health = healthFor(company, review)?.status ?? defaultHealth(company);
    const matchesSearch = !query || [company.name, company.website, company.legalName].some((value) => value?.toLowerCase().includes(query));
    const matchesResearched = !researchedFilter || researchedFilter === "researched" && Boolean(company.lastResearchedAt) || researchedFilter === "unresearched" && !company.lastResearchedAt;
    return matchesSearch && (!country || company.country === country) && (!healthFilter || health === healthFilter) && (showArchived || !company.archivedAt) && matchesResearched;
  }), [companies, country, healthFilter, researchedFilter, review, search, showArchived]);
  const healthOptions = useMemo(() => [...new Set(companies.map((company) => healthFor(company, review)?.status ?? defaultHealth(company)))].sort(), [companies, review]);

  function clearFilters() { setSearch(""); setCountry(""); setHealthFilter(""); setResearchedFilter(""); }
  function runRefresh(company: Company) { setOpenMenuId(null); navigate(`/companies/new?refreshCompanyId=${encodeURIComponent(company.id)}`); }
  function improveProfile(company: Company) { setOpenMenuId(null); navigate(`/companies/${encodeURIComponent(company.id)}?improve=true`); }
  function openMonitoring(company: Company) { setOpenMenuId(null); navigate(`/companies/${encodeURIComponent(company.id)}?tab=monitoring`); }
  function toggleActionMenu(companyId: string, event: MouseEvent<HTMLButtonElement>) {
    if (openMenuId === companyId) {
      setOpenMenuId(null);
      return;
    }

    // The menu is absolutely positioned. Open it upward near the viewport
    // bottom so it never creates a new document-height scrollbar just because
    // the final visible row's actions were opened.
    const spaceBelow = window.innerHeight - event.currentTarget.getBoundingClientRect().bottom;
    setMenuPlacement(spaceBelow < 260 ? "up" : "down");
    setOpenMenuId(companyId);
  }

  async function updateArchive(company: Company, restore: boolean) {
    setOpenMenuId(null); setBusyCompanyId(company.id); setActionError(null);
    try {
      const updated = restore ? await restoreCompany(company.id) : await archiveCompany(company.id);
      setCompanies((current) => current.map((item) => item.id === updated.id ? updated : item));
      await loadWorkspace(showArchived);
    } catch (reason) {
      setActionError(getApiErrorMessage(reason, restore ? "Could not restore this company." : "Could not archive this company."));
    } finally { setBusyCompanyId(null); }
  }

  async function permanentlyDelete() {
    if (!deleteTarget) return;
    setDeleteBusy(true); setActionError(null);
    try {
      await deleteCompany(deleteTarget.id);
      setCompanies((current) => current.filter((item) => item.id !== deleteTarget.id));
      setDeleteTarget(null);
      await loadWorkspace(showArchived);
    } catch (reason) {
      setActionError(getApiErrorMessage(reason, "Could not permanently delete this company."));
    } finally { setDeleteBusy(false); }
  }

  function beginMerge(canonicalId: string, duplicateId: string) {
    setMergePair({ canonicalId, duplicateId }); setMergePreview(null); setMergeError(null);
  }

  useEffect(() => {
    if (!mergePair) return;
    let active = true;
    setMergeLoading(true); setMergeError(null);
    previewCompanyMerge(mergePair.canonicalId, mergePair.duplicateId)
      .then((result) => { if (active) setMergePreview(result); })
      .catch((reason) => { if (active) setMergeError(getApiErrorMessage(reason, "Could not prepare the merge preview.")); })
      .finally(() => { if (active) setMergeLoading(false); });
    return () => { active = false; };
  }, [mergePair]);

  async function confirmMerge() {
    if (!mergePair) return;
    setMergeBusy(true); setMergeError(null);
    try {
      const result = await confirmCompanyMerge(mergePair.canonicalId, mergePair.duplicateId);
      const canonicalId = result.canonicalCompany.id;
      setCompanies((current) => current.filter((company) => company.id !== mergePair.duplicateId).map((company) => company.id === result.canonicalCompany.id ? result.canonicalCompany : company));
      setMergePair(null); setMergePreview(null);
      // A successful merge has a useful destination: hand the user to the
      // canonical dossier so they can verify the retained profile/evidence.
      navigate(`/companies/${encodeURIComponent(canonicalId)}`);
    } catch (reason) {
      setMergeError(getApiErrorMessage(reason, "Could not merge these companies."));
    } finally { setMergeBusy(false); }
  }

  const researchReady = useMemo(
    () => (review?.researchReady ?? []).filter((item) => !isResearchReviewAcknowledged(`${item.method}:${item.itemId}`)),
    [review, reviewAcknowledgementVersion],
  );
  const researchIssues = review?.researchIssues ?? [];
  const attentionRecommendations = review?.recommendations?.filter((item) => item.kind !== "PossibleDuplicate") ?? [];
  const workspaceAttentionCount = attentionRecommendations.length + (review?.duplicateGroups?.length ?? 0);
  const openResearchReview = (item: WorkspaceResearchReviewItem) => {
    acknowledgeResearchReview(`${item.method}:${item.itemId}`);
    setReviewAcknowledgementVersion((version) => version + 1);
    navigate(`/companies/${encodeURIComponent(item.companyId)}?tab=investigations&research=${encodeURIComponent(item.investigationId ?? item.itemId)}`);
  };
  function reviewGroup(group: CompanyDuplicateGroup) { if (group.members.length >= 2) beginMerge(group.members[0].companyId, group.members[1].companyId); }
  function reviewRecommendation(recommendation: WorkspaceReviewRecommendation) {
    if (recommendation.relatedCompanyIds.length > 0) beginMerge(recommendation.companyId, recommendation.relatedCompanyIds[0]);
    else if (recommendation.kind === "SparseProfile" || recommendation.kind === "Stale") navigate(`/companies/new?refreshCompanyId=${encodeURIComponent(recommendation.companyId)}`);
    else navigate(`/companies/${encodeURIComponent(recommendation.companyId)}?review=true`);
  }

  return <div className="page-stack">
    <div className="page-title-row"><div><p className="eyebrow">COMPANY WORKSPACE</p><h1>Company List</h1><p className="page-intro">Manage company records, profile health and research hygiene from one workspace.</p></div><div className="page-title-row__actions"><button className="button button--ai" type="button" aria-expanded={reviewOpen} onClick={() => { const next = !reviewOpen; setReviewOpen(next); if (next) void loadWorkspace(showArchived); }}><Sparkle size={17} weight="fill" aria-hidden="true" /> {reviewOpen ? "Close workspace review" : "Review workspace"}</button><Link className="button" to="/companies/new">+ Add Company Profile</Link></div></div>
    {actionError ? <div className="form-error" role="alert">{actionError}</div> : null}
    {reviewOpen ? <Panel className="workspace-review-panel" title="Workspace review" eyebrow="ATTENTION QUEUE"><div className="workspace-review-panel__header"><p>One compact queue for research results, profile health, and identity cleanup.</p><button className="button button--quiet" type="button" onClick={() => setReviewOpen(false)}>Close</button></div>{reviewLoading ? <div className="empty-state"><strong>Reviewing workspace…</strong><p>Checking identity, coverage, and recency.</p></div> : null}{reviewError ? <div className="form-error" role="alert">{reviewError}<button className="button button--quiet" type="button" onClick={() => void loadWorkspace(showArchived)}>Retry</button></div> : null}{!reviewLoading && review ? <><div className="workspace-review-overview" aria-label="Workspace review summary"><ReviewMetric label="Research ready" count={researchReady.length} detail="Open findings" tone="ready" /><ReviewMetric label="Profile attention" count={attentionRecommendations.length} detail="Health and recency" tone="attention" /><ReviewMetric label="Duplicates" count={review.duplicateGroups.length} detail="Identity cleanup" tone="duplicate" /><ReviewMetric label="Research issues" count={researchIssues.length} detail="Low priority" tone="issue" /></div><p className="workspace-review-count"><strong>{workspaceAttentionCount}</strong> workspace item{workspaceAttentionCount === 1 ? "" : "s"}<span>Research ready is acknowledged when opened.</span></p>{researchReady.length === 0 && attentionRecommendations.length === 0 && review.duplicateGroups.length === 0 && researchIssues.length === 0 ? <div className="empty-state"><strong>No records need attention.</strong><p>Completed research is acknowledged after you open it; remaining checks are clear.</p></div> : null}{researchReady.length > 0 ? <details className="workspace-review-category" open><summary><span>Research ready for review</span><strong>{researchReady.length}</strong></summary><ResearchReviewGroup title="Research ready for review" items={researchReady} icon={CheckCircle} tone="ready" onOpen={openResearchReview} showHeading={false} /></details> : null}{attentionRecommendations.length > 0 ? <details className="workspace-review-category" open={attentionRecommendations.length <= 2}><summary><span>Companies needing attention</span><strong>{attentionRecommendations.length}</strong></summary><section className="workspace-review-section"><div className="workspace-review-grid">{attentionRecommendations.map((recommendation) => <article className="workspace-review-card" key={`${recommendation.kind}-${recommendation.companyId}-${recommendation.title}`}><div className="workspace-review-card__heading"><Sparkle size={18} aria-hidden="true" /><span><strong>{companies.find((company) => company.id === recommendation.companyId)?.name ?? "Company record"}</strong><small>{recommendation.title}</small></span>{recommendation.healthStatus ? <span className={statusClass(recommendation.healthStatus)}>{statusLabel(recommendation.healthStatus)}</span> : null}</div><p>{recommendation.summary}</p><div className="workspace-review-card__actions"><button className="button button--secondary" type="button" onClick={() => reviewRecommendation(recommendation)}>{recommendation.kind === "SparseProfile" ? "Research now" : recommendation.kind === "Stale" ? "Refresh" : "Open record"}</button>{recommendation.kind === "SparseProfile" ? <button className="button button--quiet" type="button" onClick={() => { const company = companies.find((item) => item.id === recommendation.companyId); if (company) void updateArchive(company, false); }}>Archive</button> : null}</div></article>)}</div></section></details> : null}{review.duplicateGroups.length > 0 ? <details className="workspace-review-category" open={review.duplicateGroups.length <= 1}><summary><span>Possible duplicates</span><strong>{review.duplicateGroups.length}</strong></summary><section className="workspace-review-section"><div className="workspace-review-grid">{review.duplicateGroups.map((group) => <article className="workspace-review-card" key={group.groupId}><div className="workspace-review-card__heading"><GitMerge size={18} aria-hidden="true" /><span><strong>{group.members.map((member) => member.name).join(" / ")}</strong><small>{statusLabel(group.strongestMatch)}</small></span></div><p>{group.rationale}</p><button className="button button--secondary" type="button" onClick={() => reviewGroup(group)}>Review merge</button></article>)}</div></section></details> : null}{researchIssues.length > 0 ? <details className="workspace-review-category workspace-review-category--issues"><summary><span>Research issues</span><strong>{researchIssues.length}</strong></summary><ResearchReviewGroup title="Research issues" items={researchIssues} icon={WarningCircle} tone="issue" onOpen={openResearchReview} showHeading={false} /></details> : null}</> : null}</Panel> : null}
    <Panel className="directory-panel"><form className="filter-toolbar" role="search" aria-label="Company filters" onSubmit={(event) => event.preventDefault()}><label className="filter-search"><span>Search company</span><span className="filter-search__input"><MagnifyingGlass size={16} aria-hidden="true" /><input className="text-input" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search by company name" /></span></label><label className="filter-field"><span>Country</span><select className="text-input" value={country} onChange={(event) => setCountry(event.target.value)}><option value="">All countries</option>{countries.map((item) => <option key={item}>{item}</option>)}</select></label><label className="filter-field"><span>Profile health</span><select className="text-input" value={healthFilter} onChange={(event) => setHealthFilter(event.target.value)}><option value="">All health</option>{healthOptions.map((item) => <option key={item} value={item}>{statusLabel(item)}</option>)}</select></label><label className="filter-field"><span>Research status</span><select className="text-input" value={researchedFilter} onChange={(event) => setResearchedFilter(event.target.value)}><option value="">Any research status</option><option value="researched">Researched</option><option value="unresearched">Unresearched</option></select></label><button className="button button--quiet filter-clear" type="button" onClick={clearFilters}>Clear filters</button><label className="archived-toggle"><input type="checkbox" checked={showArchived} onChange={(event) => setShowArchived(event.target.checked)} /> <span>Show archived</span></label></form><div className="list-summary"><strong>{filteredCompanies.length} compan{filteredCompanies.length === 1 ? "y" : "ies"}</strong><span>{showArchived ? "Including archived records" : "Active workspace records"}</span>{review ? <span className="list-summary__review">Health review ready</span> : null}</div>{loading ? <div className="empty-state"><strong>Loading companies…</strong><p>Fetching company records from the backend.</p></div> : error ? <div className="empty-state"><strong>Could not load companies.</strong><p>{error}</p><button className="button button--secondary" type="button" onClick={() => window.location.reload()}>Retry</button></div> : filteredCompanies.length ? <div className="table-wrap"><table className="company-table workspace-company-table"><thead><tr><th scope="col">Company</th><th scope="col">Profile health</th><th scope="col">Country</th><th scope="col">Monitoring</th><th scope="col">Last researched</th><th scope="col">Actions</th></tr></thead><tbody>{filteredCompanies.map((company) => { const health = healthFor(company, review)?.status ?? defaultHealth(company); const assessment = healthFor(company, review); const busy = busyCompanyId === company.id; return <tr key={company.id} className={company.archivedAt ? "company-row--archived" : undefined}><td><div className="company-cell"><span className="company-avatar company-avatar--table" aria-hidden="true">{getInitials(company.name)}</span><span><Link className="company-link" to={`/companies/${company.id}`}>{company.name}</Link><small className="table-secondary">{company.legalName || company.website || "Company identity"}</small></span></div></td><td><span className={statusClass(health)}>{statusLabel(health)}</span>{assessment && assessment.missingTargets.length > 0 ? <small className="table-secondary health-missing">{assessment.missingTargets.length} gap{assessment.missingTargets.length === 1 ? "" : "s"}</small> : null}</td><td>{company.country || "Not provided"}</td><td><span className="status">Not configured</span></td><td>{formatDate(company.lastResearchedAt)}</td><td><div className="company-row-actions"><Link className="action-link" to={`/companies/${company.id}`}>Open</Link><div className="action-menu"><button className="action-menu__trigger" type="button" aria-label={`Actions for ${company.name}`} aria-expanded={openMenuId === company.id} onClick={(event) => toggleActionMenu(company.id, event)}><DotsThreeVertical size={20} weight="bold" aria-hidden="true" /></button>{openMenuId === company.id ? <div className={`action-menu__panel action-menu__panel--${menuPlacement}`} role="menu"><button role="menuitem" type="button" onClick={() => runRefresh(company)}>Refresh research</button><button role="menuitem" type="button" onClick={() => improveProfile(company)}>Improve profile</button><button role="menuitem" type="button" onClick={() => openMonitoring(company)}>Monitor company</button><span className="action-menu__divider" />{company.archivedAt ? <button role="menuitem" type="button" disabled={busy} onClick={() => void updateArchive(company, true)}><ArrowUUpLeft size={16} aria-hidden="true" /> Restore</button> : <button role="menuitem" type="button" disabled={busy} onClick={() => void updateArchive(company, false)}><Archive size={16} aria-hidden="true" /> Archive</button>}<button className="action-menu__danger" role="menuitem" type="button" onClick={() => { setOpenMenuId(null); setDeleteTarget(company); }}><Trash size={16} aria-hidden="true" /> Delete permanently</button></div> : null}</div></div></td></tr>; })}</tbody></table></div> : <div className="empty-state"><strong>No companies match these filters.</strong><p>Try clearing a filter or search another company.</p><button className="button button--secondary" type="button" onClick={clearFilters}>Clear filters</button></div>}</Panel>
    <WorkspaceDialog open={deleteTarget !== null} title="Delete company permanently?" labelledBy="delete-dialog-title" onClose={() => { if (!deleteBusy) setDeleteTarget(null); }}>{deleteTarget ? <><p className="workspace-dialog__intro"><strong>{deleteTarget.name}</strong> and its dependent records will be permanently removed. This cannot be undone.</p><ul className="delete-list"><li>Research runs and acquired sources</li><li>Profile versions, evidence and changes</li><li>Monitoring records and saved investigations</li></ul><div className="modal-actions"><button className="button button--secondary" type="button" disabled={deleteBusy} onClick={() => setDeleteTarget(null)}>Cancel</button><button className="button button--danger" type="button" disabled={deleteBusy} onClick={() => void permanentlyDelete()}>{deleteBusy ? "Deleting…" : "Delete permanently"}</button></div></> : null}</WorkspaceDialog>
    <MergePreviewDialog open={mergePair !== null} preview={mergePreview} loading={mergeLoading} error={mergeError} busy={mergeBusy} onClose={() => { if (!mergeBusy) setMergePair(null); }} onConfirm={() => void confirmMerge()} />
  </div>;
}
