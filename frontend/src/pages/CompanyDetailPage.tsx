import { useEffect, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { Panel } from "../components/Panel";
import { CompanyDossier } from "../components/dossier/CompanyDossier";
import { getApiErrorMessage } from "../api/client";
import { getCompany } from "../api/companies";
import { getCompanySources, getResearchRun, type ResearchRun, type SourceDocument } from "../api/research";
import { getCompanyCoverage, type EvidenceCoverageResponse } from "../api/coverage";
import { getCurrentCompanyProfile } from "../api/profiles";
import { getCompanyProfileChanges, getCompanyProfileVersions, type ProfileChange } from "../api/profileTracking";
import { getCompanyMonitoring, updateCompanyMonitoring, type CompanyMonitoring, type UpdateCompanyMonitoring } from "../api/monitoring";
import type { Company } from "../types/company";
import type { CompanyProfileVersion } from "../types/profile";
import type { ResearchTarget } from "../api/coverage";
import type { DossierTab } from "../components/dossier/dossierTypes";

const validResearchTargets: ResearchTarget[] = ["LegalIdentity", "TaxRegistration", "FoundedHistory", "Industry", "EmployeeScale", "ProductsServices", "Markets", "Leadership", "Locations"];

function parseResearchTargets(value: string | null): ResearchTarget[] {
  if (!value) return [];
  return value.split(",").map((item) => item.trim()).filter((item): item is ResearchTarget => validResearchTargets.includes(item as ResearchTarget));
}

const validDossierTabs: DossierTab[] = ["overview", "sources", "investigations", "changes", "monitoring"];

function parseDossierTab(value: string | null): DossierTab | undefined {
  return value && validDossierTabs.includes(value as DossierTab) ? value as DossierTab : undefined;
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "long", year: "numeric" }).format(new Date(value));
}

function SparseProfileGuidance({ companyId, mode }: { companyId: string; mode?: "improve" | "monitoring" }) {
  const heading = mode === "monitoring"
    ? "Monitoring needs an accepted profile"
    : mode === "improve"
      ? "This profile is not ready to improve"
      : "Build a profile before using workspace actions";

  return (
    <Panel className="narrow-page sparse-profile-guidance" title={heading} eyebrow="PROFILE NEEDS EVIDENCE">
      <p>
        RAVEN does not have an accepted Company Profile for this record yet, so targeted improvement and monitoring are not available.
        Refresh public-source research first, then review the resulting evidence before using either workspace action.
      </p>
      <div className="modal-actions">
        <Link className="button" to={`/companies/new?refreshCompanyId=${encodeURIComponent(companyId)}`}>Refresh research</Link>
        <Link className="button button--secondary" to="/companies?review=true">Review workspace</Link>
      </div>
    </Panel>
  );
}

export function CompanyDetailPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [company, setCompany] = useState<Company | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [researchRun, setResearchRun] = useState<ResearchRun | null>(null);
  const [sources, setSources] = useState<SourceDocument[]>([]);
  const [researchError, setResearchError] = useState<string | null>(null);
  const [profile, setProfile] = useState<CompanyProfileVersion | null>(null);
  const [profileVersions, setProfileVersions] = useState<CompanyProfileVersion[]>([]);
  const [profileChanges, setProfileChanges] = useState<ProfileChange[]>([]);
  const [trackingLoading, setTrackingLoading] = useState(false);
  const [trackingError, setTrackingError] = useState<string | null>(null);
  const [monitoring, setMonitoring] = useState<CompanyMonitoring | null>(null);
  const [monitoringLoading, setMonitoringLoading] = useState(false);
  const [monitoringSaving, setMonitoringSaving] = useState(false);
  const [monitoringError, setMonitoringError] = useState<string | null>(null);
  const [coverage, setCoverage] = useState<EvidenceCoverageResponse | null>(null);
  const [coverageLoading, setCoverageLoading] = useState(false);
  const [coverageError, setCoverageError] = useState<string | null>(null);
  const researchRunId = searchParams.get("researchRun");
  const improveRequested = searchParams.get("improve") === "true";
  const improveTargets = parseResearchTargets(searchParams.get("targets"));
  const requestedTab = parseDossierTab(searchParams.get("tab"));

  useEffect(() => {
    if (!id) {
      setLoading(false);
      return;
    }

    let active = true;
    getCompany(id)
      .then((result) => { if (active) setCompany(result); })
      .catch((reason: unknown) => { if (active) setError(getApiErrorMessage(reason, "Could not load this company.")); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [id]);

  useEffect(() => {
    if (!id) return;
    let active = true;
    Promise.all([getCurrentCompanyProfile(id).catch(() => null), getCompanySources(id).catch(() => [])])
      .then(([currentProfile, companySources]) => {
        if (!active) return;
        setProfile(currentProfile);
        if (!researchRunId) setSources(companySources);
      });
    return () => { active = false; };
  }, [id, researchRunId]);

  useEffect(() => {
    if (!id) return;
    let active = true;
    setTrackingLoading(true);
    setTrackingError(null);
    Promise.all([getCompanyProfileVersions(id), getCompanyProfileChanges(id)])
      .then(([versions, changes]) => {
        if (!active) return;
        setProfileVersions(versions);
        setProfileChanges(changes);
      })
      .catch((reason: unknown) => {
        if (active) setTrackingError(getApiErrorMessage(reason, "Could not load profile tracking."));
      })
      .finally(() => { if (active) setTrackingLoading(false); });
    return () => { active = false; };
  }, [id]);

  useEffect(() => {
    if (!id) return;
    let active = true;
    setCoverageLoading(true);
    setCoverageError(null);
    getCompanyCoverage(id)
      .then((result) => { if (active) setCoverage(result); })
      .catch((reason: unknown) => { if (active) setCoverageError(getApiErrorMessage(reason, "Could not load evidence coverage.")); })
      .finally(() => { if (active) setCoverageLoading(false); });
    return () => { active = false; };
  }, [id]);

  useEffect(() => {
    if (!id) return;
    let active = true;
    setMonitoringLoading(true);
    setMonitoringError(null);
    getCompanyMonitoring(id)
      .then((result) => { if (active) setMonitoring(result); })
      .catch((reason: unknown) => {
        if (active) setMonitoringError(getApiErrorMessage(reason, "Could not load monitoring settings."));
      })
      .finally(() => { if (active) setMonitoringLoading(false); });
    return () => { active = false; };
  }, [id]);

  const saveMonitoring = async (update: UpdateCompanyMonitoring) => {
    if (!id) return;
    setMonitoringSaving(true);
    setMonitoringError(null);
    try {
      setMonitoring(await updateCompanyMonitoring(id, update));
    } catch (reason) {
      setMonitoringError(getApiErrorMessage(reason, "Could not save monitoring settings."));
    } finally {
      setMonitoringSaving(false);
    }
  };

  async function handleProfileConfirmed(nextProfile: CompanyProfileVersion) {
    if (!id) return;
    setProfile(nextProfile);
    setSources(await getCompanySources(id).catch(() => sources));
    const [versions, changes, nextCoverage] = await Promise.all([
      getCompanyProfileVersions(id).catch(() => profileVersions),
      getCompanyProfileChanges(id).catch(() => profileChanges),
      getCompanyCoverage(id).catch(() => coverage),
    ]);
    setProfileVersions(versions);
    setProfileChanges(changes);
    setCoverage(nextCoverage);
    navigate(`/companies/${encodeURIComponent(id)}`, { replace: true });
  }

  useEffect(() => {
    if (!id || !researchRunId) return;

    let active = true;
    Promise.all([getResearchRun(researchRunId), getCompanySources(id)])
      .then(([run, acquiredSources]) => {
        if (!active) return;
        setResearchRun(run);
        setSources(acquiredSources.filter((source) => source.researchRunId === run.id));
      })
      .catch((reason: unknown) => {
        if (active) setResearchError(getApiErrorMessage(reason, "Could not load the research result."));
      });
    return () => { active = false; };
  }, [id, researchRunId]);

  if (loading) return <Panel className="narrow-page empty-state" title="Loading company" eyebrow="RAVEN API"><p>Fetching this company from the backend.</p></Panel>;

  if (!company) {
    return <Panel className="narrow-page empty-state" title="Company not found" eyebrow="MISSING RECORD"><p>{error || "RAVEN could not find that company record."}</p><Link className="button button--secondary" to="/companies">Return to Company List</Link></Panel>;
  }

  function handleDossierTabChange(tab: DossierTab) {
    if (!id) return;
    const nextParams = new URLSearchParams(searchParams);
    if (tab === "overview") nextParams.delete("tab");
    else nextParams.set("tab", tab);
    // A tab interaction is a new intent. Do not keep reopening the enrichment
    // dialog after the user has moved to another dossier section.
    nextParams.delete("improve");
    nextParams.delete("targets");
    const query = nextParams.toString();
    navigate(`/companies/${encodeURIComponent(id)}${query ? `?${query}` : ""}`, { replace: true });
  }

  if (profile) {
    return <CompanyDossier
      company={{ id: company.id, displayName: company.name, legalName: company.legalName, registrationNumber: company.registrationNumber, website: company.website, country: company.country, headquarters: company.headquarters, industry: profile.primaryIndustry, lastResearchedAt: company.lastResearchedAt }}
      profile={{ ...profile, publicLinks: profile.publicLinks?.map((link) => link.url) ?? [], evidenceCount: profile.evidence?.length ?? 0 }}
      sources={sources.map((source) => ({ id: source.id, url: source.url, title: source.title, domain: source.sourceDomain, kind: source.sourceKind, iconUrl: source.iconUrl, preview: source.contentPreview, retrievedAt: source.retrievedAt, crawlerProvider: source.crawlerProvider, status: "acquired" }))}
      research={researchRun ? { status: researchRun.stage === "Failed" ? "failed" : researchRun.stage === "Completed" ? "completed" : "waiting", stageLabel: researchRun.stage, runId: researchRun.id, error: researchRun.error, counters: [{ label: "Documents added", value: researchRun.documentsAdded }, { label: "Candidates", value: researchRun.uniqueCandidates }] } : { status: "completed", summary: "Current accepted dossier" }}
      tracking={{
        versions: profileVersions,
        changes: profileChanges,
        isLoading: trackingLoading,
        error: trackingError,
        onRefreshResearch: () => navigate(`/companies/new?refreshCompanyId=${encodeURIComponent(company.id)}`),
      }}
      monitoring={monitoring ? {
        monitoring,
        isLoading: monitoringLoading,
        isSaving: monitoringSaving,
        error: monitoringError,
        onUpdate: saveMonitoring,
        onResearchNow: () => navigate(`/companies/new?refreshCompanyId=${encodeURIComponent(company.id)}`),
      } : null}
      coverage={{ response: coverage, isLoading: coverageLoading, error: coverageError }}
      openEnrichment={improveRequested}
      initialEnrichmentTargets={improveTargets}
      activeTab={requestedTab ?? "overview"}
      onTabChange={handleDossierTabChange}
      onProfileConfirmed={(nextProfile) => { void handleProfileConfirmed(nextProfile); }}
    />;
  }

  const researchStatus = researchRun?.status.toLowerCase();
  return (
    <div className="page-stack company-detail-page">
      <Link className="back-link" to="/companies">← Back to Company List</Link>
      {researchRun ? <div className={`success-banner research-banner research-banner--${researchStatus}`} role="status"><strong>Research {researchRun.status.toLowerCase()}.</strong> {researchRun.status === "Completed" ? `${researchRun.sourcesCrawled} public source${researchRun.sourcesCrawled === 1 ? " was" : "s were"} acquired.` : researchRun.error || "RAVEN is processing public sources."}</div> : null}
      {researchError ? <div className="form-error" role="alert">{researchError}</div> : null}
      <SparseProfileGuidance companyId={company.id} mode={requestedTab === "monitoring" ? "monitoring" : improveRequested ? "improve" : undefined} />
      <article className="company-detail-card">
        <header className="company-detail-header"><div className="company-detail-heading"><span className="company-avatar company-avatar--large" aria-hidden="true">{company.name.slice(0, 2).toUpperCase()}</span><div><p className="eyebrow">COMPANY IDENTITY</p><h1>{company.name}</h1><p className="company-detail-subtitle">{company.country || "Country not provided"}</p></div></div></header>
        <div className="company-detail-body"><Panel title="Company overview" eyebrow="STABLE IDENTITY"><div className="detail-grid"><div><span>Country</span><strong>{company.country || "Not provided"}</strong></div><div><span>Website</span><strong>{company.website || "Not provided"}</strong></div><div><span>Created</span><strong>{formatDate(company.createdAt)}</strong></div><div><span>Last updated</span><strong>{formatDate(company.updatedAt)}</strong></div></div></Panel><Panel title="Identifiers" eyebrow="VERIFICATION"><dl className="definition-list"><div><dt>Website</dt><dd>{company.website ? <a href={company.website} target="_blank" rel="noreferrer">{company.website} ↗</a> : "Not provided"}</dd></div><div><dt>Company ID</dt><dd>{company.id}</dd></div><div><dt>Last updated</dt><dd>{formatDate(company.updatedAt)}</dd></div></dl></Panel></div>
      </article>
      {researchRun ? <Panel title="Research evidence" eyebrow="BRAVE SEARCH → CRAWL4AI LOCAL"><dl className="definition-list"><div><dt>Status</dt><dd>{researchRun.status}</dd></div><div><dt>Sources found</dt><dd>{researchRun.sourcesFound}</dd></div><div><dt>Sources acquired</dt><dd>{researchRun.sourcesCrawled}</dd></div><div><dt>Search provider</dt><dd>{researchRun.actualSearchProvider || researchRun.requestedSearchProvider}</dd></div><div><dt>Crawler</dt><dd>{researchRun.actualCrawlerProvider || researchRun.requestedCrawlerProvider}</dd></div></dl>{sources.length ? <ul className="source-list">{sources.map((source) => <li key={source.id}><a href={source.url} target="_blank" rel="noreferrer">{source.title || source.url}</a><small>{source.sourceDomain || source.crawlerProvider}</small><p>{source.contentPreview}</p></li>)}</ul> : <p className="state-message">No source documents were acquired for this research run.</p>}</Panel> : null}
    </div>
  );
}
