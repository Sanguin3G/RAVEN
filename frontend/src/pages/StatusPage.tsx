import { useEffect, useState } from "react";
import { ArrowClockwise, Brain, CheckCircle, CloudArrowDown, Database, Globe, MagnifyingGlass, Pulse, WarningCircle } from "@phosphor-icons/react";
import { Button } from "../components/Button";
import { Panel } from "../components/Panel";
import { getResearchSettings, type ResearchSettings } from "../api/settings";
import { getApiHealth, getProviderStatus, type ProviderStatus, type ProviderStatusResponse } from "../api/system";

type ServiceState = "operational" | "attention" | "checking";

function providerState(provider: ProviderStatus | undefined): ServiceState {
  if (!provider) return "checking";
  if (!provider.configured || provider.available === false) return "attention";
  return "operational";
}

function apiState(available: boolean | null): ServiceState {
  return available === null ? "checking" : available ? "operational" : "attention";
}

function StateMark({ state }: { state: ServiceState }) {
  return state === "operational"
    ? <CheckCircle size={19} weight="fill" aria-hidden="true" />
    : state === "attention"
      ? <WarningCircle size={19} weight="fill" aria-hidden="true" />
      : <Pulse size={19} weight="bold" aria-hidden="true" />;
}

function stateLabel(state: ServiceState) {
  return state === "operational" ? "Operational" : state === "attention" ? "Needs attention" : "Checking";
}

function routeLabel(providers: string[] | undefined, fallback: string) {
  return providers?.length ? providers.join(" -> ") : fallback;
}

function presetLabel(preset: ResearchSettings["providerPreset"]) {
  return preset === "Balanced" ? "RAVEN Resilient" : preset === "LocalFirst" ? "RAVEN Local First" : preset === "Cloud" ? "RAVEN Cloud" : "Custom";
}

export function StatusPage() {
  const [providers, setProviders] = useState<ProviderStatusResponse | null>(null);
  const [settings, setSettings] = useState<ResearchSettings | null>(null);
  const [apiAvailable, setApiAvailable] = useState<boolean | null>(null);
  const [isRefreshing, setRefreshing] = useState(true);

  const refresh = async () => {
    setRefreshing(true);
    const [providerResult, settingsResult, apiResult] = await Promise.allSettled([getProviderStatus(), getResearchSettings(), getApiHealth()]);
    setProviders(providerResult.status === "fulfilled" ? providerResult.value : null);
    setSettings(settingsResult.status === "fulfilled" ? settingsResult.value : null);
    setApiAvailable(apiResult.status === "fulfilled" ? apiResult.value.available : false);
    setRefreshing(false);
  };

  useEffect(() => { void refresh(); }, []);

  const configuredServices = [providers?.brave, providers?.exa, providers?.crawl4Ai, providers?.firecrawl, providers?.gemini].filter((provider) => provider?.configured);
  const systemsOperational = apiAvailable === true && configuredServices.length > 0 && configuredServices.every((provider) => providerState(provider) === "operational");
  const routeState: ServiceState = settings ? "operational" : apiAvailable === null ? "checking" : "attention";
  const services = [
    { name: "Brave Search", detail: "Public-source discovery", provider: providers?.brave, icon: MagnifyingGlass, statusUrl: "https://status.brave.app/" },
    { name: "Exa", detail: "Semantic discovery and contents", provider: providers?.exa, icon: MagnifyingGlass, statusUrl: "https://status.exa.ai/" },
    { name: "Crawl4AI Local", detail: "Local evidence acquisition", provider: providers?.crawl4Ai, icon: CloudArrowDown },
    { name: "Firecrawl", detail: "Cloud evidence acquisition", provider: providers?.firecrawl, icon: CloudArrowDown, statusUrl: "https://status.firecrawl.dev/" },
    { name: "Gemini", detail: settings ? `Profile: ${settings.profileModel} | Deep: ${settings.deepResearchModel}` : "Evidence normalization", provider: providers?.gemini, icon: Brain },
  ];

  return (
    <div className="status-page page-stack">
      <div className="status-page__hero">
        <div>
          <p className="eyebrow">RAVEN OPERATIONS</p>
          <h1>{systemsOperational ? "Core research systems are operational" : isRefreshing ? "Checking research systems" : "Research systems need attention"}</h1>
          <p className="page-intro">A live view of the API, storage path, selected route, and external research capabilities.</p>
        </div>
        <Button tone="secondary" onClick={() => void refresh()} disabled={isRefreshing}><ArrowClockwise size={17} weight="bold" /> {isRefreshing ? "Checking..." : "Refresh status"}</Button>
      </div>

      <aside className="status-page__legend" aria-label="How to read this status page">
        <Pulse size={20} weight="duotone" aria-hidden="true" />
        <div><strong>How to read this page</strong><span>Core health is checked locally. Provider cards show configuration and availability; external status links open the provider's own incident page.</span></div>
      </aside>

      <Panel title="Core pipeline" eyebrow="RAVEN REQUEST PATH" className="status-flow-panel">
        <div className="status-core-flow">
          <article className={`status-core-card status-core-card--${apiState(apiAvailable)}`}><Database size={23} weight="duotone" /><div><span><StateMark state={apiState(apiAvailable)} /> {stateLabel(apiState(apiAvailable))}</span><h2>Workspace data</h2><p>Database-backed company and research state.</p></div></article>
          <span className="status-core-arrow" aria-hidden="true">-&gt;</span>
          <article className={`status-core-card status-core-card--${apiState(apiAvailable)}`}><Pulse size={23} weight="duotone" /><div><span><StateMark state={apiState(apiAvailable)} /> {stateLabel(apiState(apiAvailable))}</span><h2>RAVEN Web API</h2><p>Health endpoint and research orchestration.</p></div></article>
          <span className="status-core-arrow" aria-hidden="true">-&gt;</span>
          <article className={`status-core-card status-core-card--${routeState}`}><MagnifyingGlass size={23} weight="duotone" /><div><span><StateMark state={routeState} /> {stateLabel(routeState)}</span><h2>Provider route</h2><p>Saved search, crawler, and model preferences.</p></div></article>
          <span className="status-core-arrow" aria-hidden="true">-&gt;</span>
          <article className={`status-core-card status-core-card--${providerState(providers?.gemini)}`}><Brain size={23} weight="duotone" /><div><span><StateMark state={providerState(providers?.gemini)} /> {stateLabel(providerState(providers?.gemini))}</span><h2>RAVEN intelligence</h2><p>Grounding, profile generation, and Deep Research.</p></div></article>
        </div>
      </Panel>

      <Panel title="Current research route" eyebrow="SAVED WORKSPACE SETTINGS" className="status-route-panel">
        {settings ? <dl className="status-route-grid">
          <div><dt>Preset</dt><dd>{presetLabel(settings.providerPreset)}</dd></div>
          <div><dt>Discovery</dt><dd>{routeLabel(settings.searchProviderPriority, "No search provider selected")}</dd></div>
          <div><dt>Acquisition</dt><dd>{routeLabel(settings.crawlerProviderPriority, "No crawler selected")}</dd></div>
          <div><dt>Grounding</dt><dd>{settings.groundingMode} | {settings.groundingModel}</dd></div>
          <div><dt>Profile model</dt><dd>{settings.profileModel}</dd></div>
          <div><dt>Deep Research</dt><dd>{settings.deepResearchModel}</dd></div>
        </dl> : <p className="status-page__note">Research settings are unavailable, so this page can only show provider health.</p>}
      </Panel>

      <Panel title="Discovery, acquisition, and AI" eyebrow="LIVE PROVIDER STATUS" className="status-flow-panel">
        <div className="status-service-grid">
          {services.map((service) => {
            const ServiceIcon = service.icon;
            const state = providerState(service.provider);
            return <article className={`status-service-card status-service-card--${state}`} key={service.name}>
              <ServiceIcon className="status-service-card__icon" size={24} weight="duotone" />
              <div><span className="status-service-card__state"><StateMark state={state} /> {stateLabel(state)}</span><h2>{service.name}</h2><p>{service.detail}</p>{service.statusUrl ? <a className="status-service-card__link" href={service.statusUrl} target="_blank" rel="noreferrer"><Globe size={14} weight="bold" /> Official status</a> : null}</div>
            </article>;
          })}
        </div>
      </Panel>

    </div>
  );
}
