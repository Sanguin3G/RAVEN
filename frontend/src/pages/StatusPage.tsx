import { useEffect, useMemo, useState } from "react";
import { ArrowClockwise, WarningCircle } from "@phosphor-icons/react";
import { Link } from "react-router-dom";
import { Button } from "../components/Button";
import { Panel } from "../components/Panel";
import { getResearchSettings, type ResearchSettings } from "../api/settings";
import { getApiHealth, getProviderHealth, getProviderStatus, type ProviderHealthResponse, type ProviderModelHealth, type ProviderStatus, type ProviderStatusResponse } from "../api/system";

type ServiceState = "operational" | "recently-healthy" | "configured" | "attention" | "checking" | "unavailable" | "not-configured";

function providerFamily(provider: string) {
  const value = provider.toLowerCase();
  if (value.includes("gemini") || value.includes("google")) return "gemini";
  if (value.includes("exa")) return "exa";
  if (value.includes("brave")) return "brave";
  if (value.includes("crawl4ai")) return "crawl4ai-local";
  return value;
}

function modelTelemetry(provider: string, health: ProviderHealthResponse | null, model?: string) {
  return health?.models.find(item => providerFamily(item.provider) === providerFamily(provider) && (!model || item.model === model));
}

function providerState(provider: ProviderStatus | undefined, health: ProviderHealthResponse | null): ServiceState {
  if (!provider) return "checking";
  if (!provider.configured) return "not-configured";
  if (provider.available === false) return "unavailable";
  if (provider.available === true) return "operational";
  const latest = health?.models.filter(item => providerFamily(item.provider) === providerFamily(provider.provider))
    .sort((left, right) => Date.parse(lastActivity(right) ?? "") - Date.parse(lastActivity(left) ?? ""))[0];
  if (latest?.state === "Degraded") return "attention";
  if (latest?.state === "RecentlyHealthy") return "recently-healthy";
  return "configured";
}

function stateLabel(state: ServiceState) {
  return ({ operational: "Operational", "recently-healthy": "Recently healthy", configured: "Configured · not used recently", attention: "Needs attention", checking: "Checking", unavailable: "Unavailable", "not-configured": "Not configured" })[state];
}

function lastActivity(item: ProviderModelHealth) {
  return [item.lastSuccessAt, item.lastFailureAt].filter((value): value is string => Boolean(value)).sort((a, b) => Date.parse(b) - Date.parse(a))[0] ?? null;
}

function timeAgo(value?: string | null) {
  if (!value) return "No recent activity";
  const elapsed = Math.max(0, Date.now() - Date.parse(value));
  if (!Number.isFinite(elapsed)) return "No recent activity";
  const minutes = Math.floor(elapsed / 60_000);
  if (minutes < 1) return "Just now";
  if (minutes < 60) return `${minutes} min${minutes === 1 ? "" : "s"} ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} hour${hours === 1 ? "" : "s"} ago`;
  return `${Math.floor(hours / 24)} days ago`;
}

function failureLabel(item: ProviderModelHealth) {
  if (item.lastFailureHttpStatus === 429 || item.lastFailureCode?.toLowerCase().includes("quota") || item.lastFailureCode?.toLowerCase().includes("resource_exhausted")) return "Rate limited";
  if (item.lastFailureHttpStatus === 503 || item.lastFailureHttpStatus === 502 || item.lastFailureCode?.toLowerCase().includes("unavailable")) return "Provider busy";
  if (item.lastFailureHttpStatus === 401 || item.lastFailureHttpStatus === 403) return "Credentials rejected";
  if (item.lastFailureHttpStatus === 400) return "Request rejected";
  return "Recent request failed";
}

function modelState(item: ProviderModelHealth | undefined) {
  if (!item) return "Configured · no recent use";
  if (item.state === "Degraded") return failureLabel(item);
  if (item.state === "RecentlyHealthy") return "Recently healthy";
  return "Configured · no recent use";
}

function providerLabel(value: string) {
  const labels: Record<string, string> = { brave: "Brave Search", exa: "Exa", "exa-agent": "Exa Agent", "exa-search": "Exa Search", "exa-contents": "Exa Contents", "crawl4ai-local": "Crawl4AI Local", gemini: "Gemini" };
  return labels[value.toLowerCase()] ?? value;
}

function operationLabel(operation: string) {
  const labels: Record<string, string> = {
    briefing_generation: "Briefing synthesis", investigation_analysis: "Investigation analysis",
    external_research_analysis: "Research analysis", profile_generation: "Company Profile",
    profile_patch_generation: "Profile improvement", company_chat: "Ask RAVEN",
    identity_resolution: "Company matching", source_relevance: "Source ranking",
    deep_research_decision: "Managed research", web_search: "Search", page_crawl: "Page acquisition",
  };
  return labels[operation] ?? operation.replaceAll("_", " ");
}

function presetLabel(preset: ResearchSettings["providerPreset"]) {
  return preset === "Resilient" ? "Balanced & resilient" : preset === "LocalFirst" ? "Local-first" : preset === "Cloud" ? "Cloud-first" : "Custom";
}

export function StatusPage() {
  const [providers, setProviders] = useState<ProviderStatusResponse | null>(null);
  const [health, setHealth] = useState<ProviderHealthResponse | null>(null);
  const [settings, setSettings] = useState<ResearchSettings | null>(null);
  const [apiAvailable, setApiAvailable] = useState<boolean | null>(null);
  const [isRefreshing, setRefreshing] = useState(true);
  const [lastRefreshedAt, setLastRefreshedAt] = useState<string | null>(null);

  const refresh = async () => {
    setRefreshing(true);
    const [providerResult, settingsResult, apiResult, healthResult] = await Promise.allSettled([getProviderStatus(), getResearchSettings(), getApiHealth(), getProviderHealth()]);
    setProviders(providerResult.status === "fulfilled" ? providerResult.value : null);
    setSettings(settingsResult.status === "fulfilled" ? settingsResult.value : null);
    setApiAvailable(apiResult.status === "fulfilled" ? apiResult.value.available : false);
    setHealth(healthResult.status === "fulfilled" ? healthResult.value : null);
    setLastRefreshedAt(healthResult.status === "fulfilled" ? healthResult.value.generatedAt : new Date().toISOString());
    setRefreshing(false);
  };

  useEffect(() => { void refresh(); }, []);

  const attention = useMemo(() => (health?.models ?? []).filter(item => item.state === "Degraded")
    .sort((left, right) => Date.parse(lastActivity(right) ?? "") - Date.parse(lastActivity(left) ?? "")), [health]);
  const externalIssueCount = new Set(attention.map(item => providerFamily(item.provider))).size;
  const primaryIssue = attention[0];
  const geminiProblem = attention.find(item => providerFamily(item.provider) === "gemini");
  const geminiRoles = settings && geminiProblem ? [
    ...(settings.groundingModel === geminiProblem.model ? ["Company matching and source ranking"] : []),
    ...(settings.profileModel === geminiProblem.model ? ["Company Profile", "Ask RAVEN (currently shares the Company Profile model)"] : []),
    ...(settings.deepResearchModel === geminiProblem.model ? ["Briefings and analysis"] : []),
  ] : [];

  const providerRows = [
    { name: "Gemini", usedFor: "AI generation", status: providers?.gemini, family: "gemini" },
    { name: "Exa", usedFor: "Search · Contents · Managed Deep Research", status: providers?.exa, family: "exa" },
    { name: "Brave Search", usedFor: "Discovery fallback", status: providers?.brave, family: "brave" },
    { name: "Crawl4AI Local", usedFor: "Local page acquisition", status: providers?.crawl4Ai, family: "crawl4ai-local" },
  ];

  const modelRows = settings ? [
    { task: "Company matching & source ranking", model: settings.groundingModel },
    { task: "Company Profile", model: settings.profileModel },
    { task: "Ask RAVEN", model: settings.profileModel, note: "Shares Company Profile model" },
    { task: "Briefings & analysis", model: settings.deepResearchModel },
  ] : [];

  return <div className="status-page page-stack">
    <header className="status-page__hero">
      <div><p className="eyebrow">RAVEN OPERATIONS</p><h1>{apiAvailable === false ? "Workspace unavailable" : "System status"}</h1><p className="page-intro">{apiAvailable === false ? "RAVEN could not reach the local API and workspace database." : externalIssueCount ? `Workspace is operational · ${externalIssueCount} external provider${externalIssueCount === 1 ? "" : "s"} need attention.` : "Workspace is operational. External providers show health only after real RAVEN requests."}</p><small className="status-page__refreshed">{lastRefreshedAt ? `Last refreshed ${timeAgo(lastRefreshedAt)}` : "Status not yet refreshed"}</small></div>
      <div className="status-page__hero-actions"><Button tone="secondary" onClick={() => void refresh()} disabled={isRefreshing}><ArrowClockwise size={17} weight="bold" /> {isRefreshing ? "Refreshing…" : "Refresh"}</Button><Link className="button button--quiet" to="/settings">View settings</Link></div>
    </header>

    {primaryIssue ? <aside className="status-attention" role="alert">
      <div className="status-attention__heading"><WarningCircle size={22} weight="fill" aria-hidden="true" /><div><p className="eyebrow">{providerLabel(primaryIssue.provider).toUpperCase()} NEEDS ATTENTION</p><h2>{failureLabel(primaryIssue)}</h2></div></div>
      <p>{primaryIssue.lastFailureSummary ?? `${providerLabel(primaryIssue.provider)} recently refused a request.`} {primaryIssue.model ? `Model: ${primaryIssue.model}.` : ""} {primaryIssue.lastFailureAt ? `Observed ${timeAgo(primaryIssue.lastFailureAt)}.` : ""}</p>
      {providerFamily(primaryIssue.provider) === "gemini" && geminiRoles.length ? <p><strong>Affected tasks:</strong> {geminiRoles.join(" · ")}.</p> : null}
      {providerFamily(primaryIssue.provider) === "gemini" && primaryIssue.requestsLastMinute > 0 ? <p>RAVEN sent {primaryIssue.requestsLastMinute} request{primaryIssue.requestsLastMinute === 1 ? "" : "s"} using this model in the past minute.</p> : null}
      <div className="status-attention__actions"><Link className="button" to="/settings">{providerFamily(primaryIssue.provider) === "gemini" ? "Change AI models" : "Review provider settings"}</Link>{providerFamily(primaryIssue.provider) === "gemini" ? <a className="button button--quiet" href="https://aistudio.google.com/rate-limit?timeRange=last-28-days" target="_blank" rel="noreferrer">View Gemini limits ↗</a> : null}</div>
      <details><summary>Technical details</summary><p>{primaryIssue.lastFailureHttpStatus ? `HTTP ${primaryIssue.lastFailureHttpStatus}` : "No HTTP status recorded"}{primaryIssue.lastFailureCode ? ` · ${primaryIssue.lastFailureCode}` : ""}</p></details>
    </aside> : null}

    <Panel title="Core services" eyebrow="LOCAL WORKSPACE">
      <div className="status-core-list">
        <div><span><i className={`status-mark ${apiAvailable === true ? "status-mark--good" : apiAvailable === false ? "status-mark--bad" : ""}`} />Workspace API & database</span><strong>{apiAvailable === null ? "Checking" : apiAvailable ? "Operational" : "Unavailable"}</strong></div>
        <div><span><i className={`status-mark ${providerState(providers?.crawl4Ai, health) === "operational" ? "status-mark--good" : providerState(providers?.crawl4Ai, health) === "unavailable" ? "status-mark--bad" : ""}`} />Crawl4AI Local</span><strong>{stateLabel(providerState(providers?.crawl4Ai, health))}</strong></div>
      </div>
    </Panel>

    <Panel title="Active configuration" eyebrow="SAVED WORKSPACE SETTINGS">
      {settings ? <dl className="status-route-grid">
        <div><dt>Search</dt><dd>{settings.searchProviderPriority.map(providerLabel).join(" → ")}</dd></div>
        <div><dt>Page acquisition</dt><dd>{settings.crawlerProviderPriority.map(providerLabel).join(" → ")}</dd></div>
        <div><dt>Managed Deep Research</dt><dd>{settings.managedResearchProvider === "exa-agent" ? "Exa Agent" : settings.managedResearchProvider} · {settings.managedResearchDepth}</dd></div>
      </dl> : <p className="status-page__note">Saved research settings are unavailable.</p>}
    </Panel>

    <Panel title="AI task models" eyebrow="CONFIGURED MODEL · RECENT HEALTH">
      {modelRows.length ? <div className="status-table-wrap"><table className="status-table"><thead><tr><th scope="col">RAVEN task</th><th scope="col">Model</th><th scope="col">Recent health</th></tr></thead><tbody>{modelRows.map(row => {
        const model = modelTelemetry("gemini", health, row.model);
        return <tr key={row.task}><th scope="row">{row.task}{row.note ? <small>{row.note}</small> : null}</th><td>{row.model}</td><td><span className={`status-inline status-inline--${model?.state === "Degraded" ? "warning" : model?.state === "RecentlyHealthy" ? "good" : "muted"}`}>{modelState(model)}</span>{model?.state === "Degraded" && model.lastFailureAt ? <small>Last failure {timeAgo(model.lastFailureAt)}</small> : model?.lastSuccessAt ? <small>Last success {timeAgo(model.lastSuccessAt)}</small> : null}</td></tr>;
      })}</tbody></table></div> : <p className="status-page__note">AI task assignments are unavailable.</p>}
      <p className="status-page__note">Gemini limits are project- and model-specific. These counts reflect RAVEN telemetry, not all use of your Google project.</p>
    </Panel>

    <Panel title="External providers" eyebrow="CONFIGURATION · REAL REQUEST HEALTH">
      <div className="status-table-wrap"><table className="status-table"><thead><tr><th scope="col">Provider</th><th scope="col">Used for</th><th scope="col">Status</th><th scope="col">Last activity</th></tr></thead><tbody>{providerRows.map(row => {
        const state = providerState(row.status, health);
        const latest = health?.models.filter(item => providerFamily(item.provider) === row.family).sort((left, right) => Date.parse(lastActivity(right) ?? "") - Date.parse(lastActivity(left) ?? ""))[0];
        return <tr key={row.name}><th scope="row">{row.name}</th><td>{row.usedFor}</td><td><span className={`status-inline status-inline--${state === "attention" || state === "unavailable" || state === "not-configured" ? "warning" : state === "operational" || state === "recently-healthy" ? "good" : "muted"}`}>{stateLabel(state)}</span></td><td>{latest ? timeAgo(lastActivity(latest)) : state === "operational" ? "Checked just now" : "No request in the last 24 hours"}</td></tr>;
      })}</tbody></table></div>
      <p className="status-page__note">Remote providers are never called just to render this page. “Configured” means credentials are present, not that RAVEN has recently used them successfully.</p>
    </Panel>

    <Panel title="Recent provider activity" eyebrow="LAST 24 HOURS">
      {health?.recentActivity.length ? <div className="status-table-wrap"><table className="status-table"><thead><tr><th scope="col">When</th><th scope="col">Provider / model</th><th scope="col">RAVEN task</th><th scope="col">Result</th></tr></thead><tbody>{health.recentActivity.map((item, index) => <tr key={`${item.timestamp}-${item.provider}-${index}`}><td>{timeAgo(item.timestamp)}</td><td>{providerLabel(item.provider)}{item.model ? <small>{item.model}</small> : null}</td><td>{operationLabel(item.operation)}</td><td><span className={`status-inline status-inline--${item.status === "Failed" ? "warning" : item.status === "Completed" ? "good" : "muted"}`}>{item.failureKind === "RateLimited" ? "Rate limited" : item.failureKind === "ProviderBusy" ? "Provider busy" : item.status === "Completed" ? "Succeeded" : item.status === "Failed" ? "Failed" : item.status}</span></td></tr>)}</tbody></table></div> : <p className="status-page__note">No provider requests are recorded in the last 24 hours. Configured remote services have not been probed from this page.</p>}
    </Panel>
  </div>;
}
