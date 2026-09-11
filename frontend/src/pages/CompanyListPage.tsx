import { useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { Archive } from "@phosphor-icons/react/dist/csr/Archive";
import { ArrowUUpLeft } from "@phosphor-icons/react/dist/csr/ArrowUUpLeft";
import { DotsThreeVertical } from "@phosphor-icons/react/dist/csr/DotsThreeVertical";
import { GitMerge } from "@phosphor-icons/react/dist/csr/GitMerge";
import { MagnifyingGlass } from "@phosphor-icons/react/dist/csr/MagnifyingGlass";
import { Sparkle } from "@phosphor-icons/react/dist/csr/Sparkle";
import { Trash } from "@phosphor-icons/react/dist/csr/Trash";
import { Link, useNavigate } from "react-router-dom";
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
  type WorkspaceReviewRecommendation,
  type WorkspaceReviewResponse,
} from "../api/workspace";
import type { Company } from "../types/company";

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
  const [reviewOpen, setReviewOpen] = useState(false);
  const [openMenuId, setOpenMenuId] = useState<string | null>(null);
  const [busyCompanyId, setBusyCompanyId] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<Company | null>(null);
  const [deleteBusy, setDeleteBusy] = useState(false);
  const [mergePair, setMergePair] = useState<{ canonicalId: string; duplicateId: string } | null>(null);
  const [mergePreview, setMergePreview] = useState<CompanyMergePreview | null>(null);
  const [mergeLoading, setMergeLoading] = useState(false);
  const [mergeBusy, setMergeBusy] = useState(false);
  const [mergeError, setMergeError] = useState<string | null>(null);

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
      setCompanies((current) => current.filter((company) => company.id !== mergePair.duplicateId).map((company) => company.id === result.canonicalCompany.id ? result.canonicalCompany : company));
      setMergePair(null); setMergePreview(null);
      await loadWorkspace(showArchived);
    } catch (reason) {
      setMergeError(getApiErrorMessage(reason, "Could not merge these companies."));
    } finally { setMergeBusy(false); }
  }

  const displayedReviewCount = (review?.recommendations?.length ?? 0) + (review?.duplicateGroups?.length ?? 0);
  function reviewGroup(group: CompanyDuplicateGroup) { if (group.members.length >= 2) beginMerge(group.members[0].companyId, group.members[1].companyId); }
  function reviewRecommendation(recommendation: WorkspaceReviewRecommendation) {
    if (recommendation.relatedCompanyIds.length > 0) beginMerge(recommendation.companyId, recommendation.relatedCompanyIds[0]);
    else if (recommendation.kind === "SparseProfile" || recommendation.kind === "Stale") navigate(`/companies/new?refreshCompanyId=${encodeURIComponent(recommendation.companyId)}`);
    else navigate(`/companies/${encodeURIComponent(recommendation.companyId)}?review=true`);
  }

  return <div className="page-stack">
    <div className="page-title-row"><div><p className="eyebrow">COMPANY WORKSPACE</p><h1>Company List</h1><p className="page-intro">Manage company records, profile health and research hygiene from one workspace.</p></div><div className="page-title-row__actions"><button className="button button--ai" type="button" onClick={() => { setReviewOpen(true); void loadWorkspace(showArchived); }}><Sparkle size={17} weight="fill" aria-hidden="true" /> Review workspace</button><Link className="button" to="/companies/new">+ Add Company Profile</Link></div></div>
    {actionError ? <div className="form-error" role="alert">{actionError}</div> : null}
    {reviewOpen ? <Panel className="workspace-review-panel" title="Workspace review" eyebrow="RAVEN RECOMMENDATIONS"><div className="workspace-review-panel__header"><p>RAVEN only recommends records worth reviewing. Archive, delete and merge actions always require your confirmation.</p><button className="button button--quiet" type="button" onClick={() => setReviewOpen(false)}>Close review</button></div>{reviewLoading ? <div className="empty-state"><strong>Reviewing workspace…</strong><p>Checking health, duplicates and stale records.</p></div> : null}{reviewError ? <div className="form-error" role="alert">{reviewError}<button className="button button--quiet" type="button" onClick={() => void loadWorkspace(showArchived)}>Retry</button></div> : null}{!reviewLoading && review && displayedReviewCount === 0 ? <div className="empty-state"><strong>No records need attention.</strong><p>Deterministic workspace checks found no duplicate, sparse or stale records.</p></div> : null}{!reviewLoading && review && displayedReviewCount > 0 ? <><p className="workspace-review-count">RAVEN found <strong>{displayedReviewCount}</strong> record{displayedReviewCount === 1 ? "" : "s"} worth reviewing.</p>{review.duplicateGroups.length > 0 ? <section className="workspace-review-section"><h3>Possible duplicates</h3><div className="workspace-review-grid">{review.duplicateGroups.map((group) => <article className="workspace-review-card" key={group.groupId}><div className="workspace-review-card__heading"><GitMerge size={18} aria-hidden="true" /><strong>{group.members.length} matching records</strong><span className="status">{statusLabel(group.strongestMatch)}</span></div><p>{group.rationale}</p><ul className="workspace-member-list">{group.members.map((member) => <li key={member.companyId}><span className="company-avatar company-avatar--table" aria-hidden="true">{getInitials(member.name)}</span><span><strong>{member.name}</strong><small>{member.country || "Country not provided"}{member.website ? ` · ${member.website}` : ""}</small></span></li>)}</ul><button className="button button--secondary" type="button" onClick={() => reviewGroup(group)}>Review merge</button></article>)}</div></section> : null}{review.recommendations.filter((recommendation) => recommendation.kind !== "PossibleDuplicate").length > 0 ? <section className="workspace-review-section"><h3>Other records worth reviewing</h3><div className="workspace-review-grid">{review.recommendations.filter((recommendation) => recommendation.kind !== "PossibleDuplicate").map((recommendation) => <article className="workspace-review-card" key={`${recommendation.kind}-${recommendation.companyId}-${recommendation.title}`}><div className="workspace-review-card__heading"><Sparkle size={18} aria-hidden="true" /><strong>{recommendation.title}</strong>{recommendation.healthStatus ? <span className={statusClass(recommendation.healthStatus)}>{statusLabel(recommendation.healthStatus)}</span> : null}</div><p>{recommendation.summary}</p>{recommendation.reasons.length > 0 ? <ul className="review-reasons">{recommendation.reasons.slice(0, 3).map((reason) => <li key={reason}>{reason}</li>)}</ul> : null}<div className="workspace-review-card__actions"><button className="button button--secondary" type="button" onClick={() => reviewRecommendation(recommendation)}>{recommendation.kind === "SparseProfile" ? "Research now" : recommendation.kind === "Stale" ? "Refresh" : "Open record"}</button>{recommendation.kind === "SparseProfile" ? <button className="button button--quiet" type="button" onClick={() => { const company = companies.find((item) => item.id === recommendation.companyId); if (company) void updateArchive(company, false); }}>Archive</button> : null}</div></article>)}</div></section> : null}</> : null}</Panel> : null}
    <Panel className="directory-panel"><form className="filter-toolbar" role="search" aria-label="Company filters" onSubmit={(event) => event.preventDefault()}><label className="filter-search"><span>Search company</span><span className="filter-search__input"><MagnifyingGlass size={16} aria-hidden="true" /><input className="text-input" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search by company name" /></span></label><label className="filter-field"><span>Country</span><select className="text-input" value={country} onChange={(event) => setCountry(event.target.value)}><option value="">All countries</option>{countries.map((item) => <option key={item}>{item}</option>)}</select></label><label className="filter-field"><span>Profile health</span><select className="text-input" value={healthFilter} onChange={(event) => setHealthFilter(event.target.value)}><option value="">All health</option>{healthOptions.map((item) => <option key={item} value={item}>{statusLabel(item)}</option>)}</select></label><label className="filter-field"><span>Research status</span><select className="text-input" value={researchedFilter} onChange={(event) => setResearchedFilter(event.target.value)}><option value="">Any research status</option><option value="researched">Researched</option><option value="unresearched">Unresearched</option></select></label><button className="button button--quiet filter-clear" type="button" onClick={clearFilters}>Clear filters</button><label className="archived-toggle"><input type="checkbox" checked={showArchived} onChange={(event) => setShowArchived(event.target.checked)} /> <span>Show archived</span></label></form><div className="list-summary"><strong>{filteredCompanies.length} compan{filteredCompanies.length === 1 ? "y" : "ies"}</strong><span>{showArchived ? "Including archived records" : "Active workspace records"}</span>{review ? <span className="list-summary__review">Health review ready</span> : null}</div>{loading ? <div className="empty-state"><strong>Loading companies…</strong><p>Fetching company records from the backend.</p></div> : error ? <div className="empty-state"><strong>Could not load companies.</strong><p>{error}</p><button className="button button--secondary" type="button" onClick={() => window.location.reload()}>Retry</button></div> : filteredCompanies.length ? <div className="table-wrap"><table className="company-table workspace-company-table"><thead><tr><th scope="col">Company</th><th scope="col">Profile health</th><th scope="col">Country</th><th scope="col">Monitoring</th><th scope="col">Last researched</th><th scope="col">Actions</th></tr></thead><tbody>{filteredCompanies.map((company) => { const health = healthFor(company, review)?.status ?? defaultHealth(company); const assessment = healthFor(company, review); const busy = busyCompanyId === company.id; return <tr key={company.id} className={company.archivedAt ? "company-row--archived" : undefined}><td><div className="company-cell"><span className="company-avatar company-avatar--table" aria-hidden="true">{getInitials(company.name)}</span><span><Link className="company-link" to={`/companies/${company.id}`}>{company.name}</Link><small className="table-secondary">{company.legalName || company.website || "Company identity"}</small></span></div></td><td><span className={statusClass(health)}>{statusLabel(health)}</span>{assessment && assessment.missingTargets.length > 0 ? <small className="table-secondary health-missing">{assessment.missingTargets.length} gap{assessment.missingTargets.length === 1 ? "" : "s"}</small> : null}</td><td>{company.country || "Not provided"}</td><td><span className="status">Not configured</span></td><td>{formatDate(company.lastResearchedAt)}</td><td><div className="company-row-actions"><Link className="action-link" to={`/companies/${company.id}`}>Open</Link><div className="action-menu"><button className="action-menu__trigger" type="button" aria-label={`Actions for ${company.name}`} aria-expanded={openMenuId === company.id} onClick={() => setOpenMenuId((current) => current === company.id ? null : company.id)}><DotsThreeVertical size={20} weight="bold" aria-hidden="true" /></button>{openMenuId === company.id ? <div className="action-menu__panel" role="menu"><Link role="menuitem" to={`/companies/${company.id}`} onClick={() => setOpenMenuId(null)}>Open company</Link><button role="menuitem" type="button" onClick={() => runRefresh(company)}>Refresh research</button><button role="menuitem" type="button" onClick={() => improveProfile(company)}>Improve profile</button><button role="menuitem" type="button" onClick={() => openMonitoring(company)}>Monitor company</button><span className="action-menu__divider" />{company.archivedAt ? <button role="menuitem" type="button" disabled={busy} onClick={() => void updateArchive(company, true)}><ArrowUUpLeft size={16} aria-hidden="true" /> Restore</button> : <button role="menuitem" type="button" disabled={busy} onClick={() => void updateArchive(company, false)}><Archive size={16} aria-hidden="true" /> Archive</button>}<button className="action-menu__danger" role="menuitem" type="button" onClick={() => { setOpenMenuId(null); setDeleteTarget(company); }}><Trash size={16} aria-hidden="true" /> Delete permanently</button></div> : null}</div></div></td></tr>; })}</tbody></table></div> : <div className="empty-state"><strong>No companies match these filters.</strong><p>Try clearing a filter or search another company.</p><button className="button button--secondary" type="button" onClick={clearFilters}>Clear filters</button></div>}</Panel>
    <WorkspaceDialog open={deleteTarget !== null} title="Delete company permanently?" labelledBy="delete-dialog-title" onClose={() => { if (!deleteBusy) setDeleteTarget(null); }}>{deleteTarget ? <><p className="workspace-dialog__intro"><strong>{deleteTarget.name}</strong> and its dependent records will be permanently removed. This cannot be undone.</p><ul className="delete-list"><li>Research runs and acquired sources</li><li>Profile versions, evidence and changes</li><li>Monitoring records and saved investigations</li></ul><div className="modal-actions"><button className="button button--secondary" type="button" disabled={deleteBusy} onClick={() => setDeleteTarget(null)}>Cancel</button><button className="button button--danger" type="button" disabled={deleteBusy} onClick={() => void permanentlyDelete()}>{deleteBusy ? "Deleting…" : "Delete permanently"}</button></div></> : null}</WorkspaceDialog>
    <MergePreviewDialog open={mergePair !== null} preview={mergePreview} loading={mergeLoading} error={mergeError} busy={mergeBusy} onClose={() => { if (!mergeBusy) setMergePair(null); }} onConfirm={() => void confirmMerge()} />
  </div>;
}
