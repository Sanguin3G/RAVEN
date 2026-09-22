import { useEffect, useState } from "react";
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom";
import { Panel } from "../components/Panel";
import { CompanyDossier } from "../features/company-workspace/CompanyDossier";
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
import type { DossierTab } from "../features/company-workspace/dossierTypes";

const validResearchTargets: ResearchTarget[] = ["LegalIdentity", "TaxRegistration", "FoundedHistory", "Industry", "EmployeeScale", "ProductsServices", "Markets", "Leadership", "Locations"];

function parseResearchTargets(value: string | null): ResearchTarget[] {
  if (!value) return [];
  return value.split(",").map((item) => item.trim()).filter((item): item is ResearchTarget => validResearchTargets.includes(item as ResearchTarget));
}

const validDossierTabs: DossierTab[] = ["overview", "sources", "investigations", "changes", "monitoring"];

function parseDossierTab(value: string | null): DossierTab | undefined {
  return value && validDossierTabs.includes(value as DossierTab) ? value as DossierTab : undefined;
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
  const improveArtifactId = searchParams.get("artifact");
  const improveManagedInvestigationId = searchParams.get("managedInvestigation");
  const initialChatCapability = searchParams.get("chat") === "deep" ? "deepResearch" as const : undefined;
  const initialChatQuestion = searchParams.get("question");
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
    const nextParams = new URLSearchParams();
    if (requestedTab && requestedTab !== "overview") nextParams.set("tab", requestedTab);
    const query = nextParams.toString();
    navigate(`/companies/${encodeURIComponent(id)}${query ? `?${query}` : ""}`, { replace: true });
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
    nextParams.delete("artifact");
    nextParams.delete("managedInvestigation");
    nextParams.delete("chat");
    nextParams.delete("question");
    const query = nextParams.toString();
    navigate(`/companies/${encodeURIComponent(id)}${query ? `?${query}` : ""}`, { replace: true });
  }

  return <CompanyDossier
      company={{ id: company.id, displayName: company.name, legalName: company.legalName, registrationNumber: company.registrationNumber, website: company.website, country: company.country, headquarters: company.headquarters, industry: profile?.primaryIndustry, lastResearchedAt: company.lastResearchedAt }}
      profile={profile ? { ...profile, publicLinks: profile.publicLinks?.map((link) => link.url) ?? [], evidenceCount: profile.evidence?.length ?? 0 } : null}
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
      initialEnrichmentArtifactId={improveArtifactId}
      initialManagedResearchInvestigationId={improveManagedInvestigationId}
      initialChatCapability={initialChatCapability}
      initialChatQuestion={initialChatQuestion}
      activeTab={requestedTab ?? "overview"}
      onTabChange={handleDossierTabChange}
      onOpenProfileImprovement={(targets, sourceMaterialId, sourceMaterialKind) => {
        const params = new URLSearchParams({ improve: "true", targets: targets.join(",") });
        if (sourceMaterialKind === "managed" && sourceMaterialId) params.set("managedInvestigation", sourceMaterialId);
        else if (sourceMaterialId) params.set("artifact", sourceMaterialId);
        navigate(`/companies/${encodeURIComponent(company.id)}?${params.toString()}`);
      }}
      onOpenDeepResearch={(objective) => {
        const params = new URLSearchParams({ tab: "investigations", chat: "deep" });
        if (objective) params.set("question", objective);
        navigate(`/companies/${encodeURIComponent(company.id)}?${params.toString()}`);
      }}
      onProfileConfirmed={(nextProfile) => { void handleProfileConfirmed(nextProfile); }}
    />;
}
