import type { ReactNode } from "react";
import { CaretRight } from "@phosphor-icons/react";
import { Panel } from "../../components/Panel";
import type { ProviderStatus, ProviderStatusResponse } from "../../api/system";
import type { ProviderPreset, ResearchSettings } from "../../api/settings";
import styles from "../settings.module.css";

const presetChoices: Array<{ value: ProviderPreset; title: string; description: string }> = [
  { value: "Resilient", title: "Balanced & resilient · Recommended", description: "Local and low-cost providers first, with cloud fallback for transient failures." },
  { value: "LocalFirst", title: "Local-first", description: "Brave Search and local Crawl4AI only; lowest external usage." },
  { value: "Cloud", title: "Cloud-first", description: "Exa Search and hosted page acquisition." },
  { value: "Custom", title: "Custom", description: "Choose the exact search and page-reading order." },
];

const customSearchProviders = ["brave", "exa"];
const customCrawlerProviders = ["crawl4ai-local", "exa"];

function providerStatusLabel(provider: ProviderStatus | undefined) {
  if (!provider) return "Checking…";
  if (!provider.configured) return "Not configured";
  if (provider.available === false) return "Unavailable";
  if (provider.available == null) return "Configured";
  return "Operational";
}

function providerStatusClass(provider: ProviderStatus | undefined) {
  if (!provider) return "";
  if (!provider.configured || provider.available === false) return styles["statusDot--warning"];
  if (provider.available == null) return styles["statusDot--checking"];
  return styles["statusDot--ok"];
}

export function displayProvider(value: string) {
  const labels: Record<string, string> = {
    brave: "Brave Search",
    exa: "Exa Search & Contents",
    "crawl4ai-local": "Crawl4AI Local",
    "crawl4ai-cloud": "Crawl4AI Cloud",
  };
  return labels[value] ?? value;
}

type Props = {
  draft: ResearchSettings;
  disabled: boolean;
  providers: ProviderStatusResponse | null;
  selectPreset: (preset: ProviderPreset) => void;
  customPriorityEditor: (kind: "searchProviderPriority" | "crawlerProviderPriority", label: string, options: string[]) => ReactNode;
};

export function ResearchProviderSettings({ draft, disabled, providers, selectPreset, customPriorityEditor }: Props) {
  return (
<Panel title="Research route" eyebrow="RESEARCH PROVIDERS" className={styles.section}>
            <div className={styles.sectionIntro}><p>Choose the provider route RAVEN should try. Cloud providers are not probed with billable requests from this page.</p></div>
            <fieldset className={styles.fieldSet} disabled={disabled}><legend>Starting route</legend><div className={styles.presetGrid}>{presetChoices.map(choice => <label className={`${styles.preset} ${draft.providerPreset === choice.value ? styles["preset--active"] : ""}`} key={choice.value}><input className="sr-only" type="radio" name="provider-preset" value={choice.value} checked={draft.providerPreset === choice.value} onChange={() => selectPreset(choice.value)} /><strong>{choice.title}</strong><span>{choice.description}</span></label>)}</div></fieldset>
            <div className={styles.routePreview}><strong>Your route</strong><span><small>Search</small>{draft.searchProviderPriority.map(displayProvider).join(" → ")}</span><span><small>Read pages</small>{draft.crawlerProviderPriority.map(displayProvider).join(" → ")}</span></div>
            {draft.providerPreset === "Custom" ? <details className={styles.customRoutingDisclosure}>
              <summary><CaretRight size={16} weight="bold" aria-hidden="true" /><strong>Custom routing</strong><small>Choose exact provider priority.</small></summary>
              <div className={styles.customRoutingGrid}>
                {customPriorityEditor("searchProviderPriority", "Search", customSearchProviders)}
                {customPriorityEditor("crawlerProviderPriority", "Read pages", customCrawlerProviders)}
              </div>
              <small>First available provider is used.</small>
            </details> : null}
            <div className={styles.providerArea}><h3>Connection configuration</h3><div className={styles.providerGrid} aria-live="polite">{[["Brave Search", providers?.brave], ["Crawl4AI Local", providers?.crawl4Ai], ["Exa", providers?.exa], ["Gemini", providers?.gemini]].map(([name, provider]) => { const typedProvider = provider as ProviderStatus | undefined; return <div className={styles.providerStatus} key={name as string}><span className={`${styles.statusDot} ${providerStatusClass(typedProvider)}`} aria-hidden="true" /><span><strong>{name as string}</strong><small>{providerStatusLabel(typedProvider)}</small></span></div>; })}</div></div>
            <p className={styles.catalogNote}>For recent request health and rate limits, see <a href="/status">System Status</a>.</p>
          </Panel>
  );
}
