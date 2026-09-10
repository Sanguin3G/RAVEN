import { useEffect, useState } from "react";
import { ArrowClockwise, Brain, CheckCircle, CloudArrowDown, MagnifyingGlass, Pulse, WarningCircle } from "@phosphor-icons/react";
import { Button } from "../components/Button";
import { Panel } from "../components/Panel";
import { getProviderStatus, type ProviderStatus, type ProviderStatusResponse } from "../api/system";

type ServiceState = "operational" | "attention" | "checking";

function providerState(provider: ProviderStatus | undefined): ServiceState {
  if (!provider) return "checking";
  if (!provider.configured || provider.available === false) return "attention";
  return "operational";
}

function StateMark({ state }: { state: ServiceState }) {
  return state === "operational"
    ? <CheckCircle size={20} weight="fill" />
    : state === "attention"
      ? <WarningCircle size={20} weight="fill" />
      : <Pulse size={20} weight="bold" />;
}

export function StatusPage() {
  const [providers, setProviders] = useState<ProviderStatusResponse | null>(null);
  const [isRefreshing, setRefreshing] = useState(true);

  const refresh = async () => {
    setRefreshing(true);
    try { setProviders(await getProviderStatus()); } catch { setProviders(null); } finally { setRefreshing(false); }
  };

  useEffect(() => { void refresh(); }, []);

  const services = [
    { name: "Brave Search", detail: "Discovers public source candidates", provider: providers?.brave, icon: MagnifyingGlass },
    { name: "Crawl4AI Local", detail: "Reads selected evidence pages", provider: providers?.crawl4Ai, icon: CloudArrowDown },
    { name: "Gemini", detail: providers?.gemini.selectedModel ? `Fast model: ${providers.gemini.selectedModel}` : "Normalizes acquired evidence", provider: providers?.gemini, icon: Brain },
  ];
  const operational = services.every((service) => providerState(service.provider) === "operational");

  return (
    <div className="status-page page-stack">
      <div className="status-page__hero">
        <div>
          <p className="eyebrow">RAVEN OPERATIONS</p>
          <h1>{operational ? "Research systems are operational" : isRefreshing ? "Checking research systems" : "Research systems need attention"}</h1>
          <p className="page-intro">A truthful view of the local services that turn discovery into evidence and a Company Profile.</p>
        </div>
        <Button tone="secondary" onClick={() => void refresh()} disabled={isRefreshing}><ArrowClockwise size={17} weight="bold" /> {isRefreshing ? "Checking…" : "Refresh status"}</Button>
      </div>

      <Panel title="Research flow" eyebrow="PUBLIC-SOURCE PIPELINE" className="status-flow-panel">
        <ol className="status-flow">
          <li className="status-flow__origin"><span>Company</span><small>identity and research hints</small></li>
          {services.map((service) => {
            const ServiceIcon = service.icon;
            const state = providerState(service.provider);
            return <li className={`status-flow__step status-flow__step--${state}`} key={service.name}>
              <span className="status-flow__icon"><ServiceIcon size={23} weight={state === "operational" ? "fill" : "regular"} /></span>
              <span><strong>{service.name}</strong><small>{service.detail}</small></span>
              <StateMark state={state} />
            </li>;
          })}
          <li className="status-flow__destination"><span>Company dossier</span><small>profile with source provenance</small></li>
        </ol>
      </Panel>

      <div className="status-service-grid">
        {services.map((service) => {
          const ServiceIcon = service.icon;
          const state = providerState(service.provider);
          const label = state === "operational" ? "Operational" : state === "attention" ? "Needs attention" : "Checking";
          return <article className={`status-service-card status-service-card--${state}`} key={service.name}>
            <ServiceIcon className="status-service-card__icon" size={24} weight="duotone" />
            <div><span className="status-service-card__state"><StateMark state={state} /> {label}</span><h2>{service.name}</h2><p>{service.detail}</p></div>
          </article>;
        })}
      </div>

      <Panel title="What this page does—and does not—mean" eyebrow="STATUS SCOPE">
        <p className="status-page__note">Operational means RAVEN can see the configured provider or local crawler availability. It does not guarantee that every public page will allow crawling; source-level failures remain visible in the research workspace.</p>
      </Panel>
    </div>
  );
}
