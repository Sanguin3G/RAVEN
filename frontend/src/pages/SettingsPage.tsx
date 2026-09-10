import { useEffect, useState } from "react";
import { Panel } from "../components/Panel";
import { ThemeSelector } from "../components/ThemeSelector";
import { getProviderStatus, type ProviderStatusResponse } from "../api/system";

export function SettingsPage() {
  const [providers, setProviders] = useState<ProviderStatusResponse | null>(null);

  useEffect(() => {
    let active = true;
    getProviderStatus().then((result) => { if (active) setProviders(result); }).catch(() => { if (active) setProviders(null); });
    return () => { active = false; };
  }, []);

  const status = (configured?: boolean, available?: boolean | null) => !configured ? "Not configured" : available === false ? "Unavailable" : "Configured";
  const hasProviderStatus = Boolean(providers?.brave && providers?.crawl4Ai && providers?.gemini);
  return (
    <div className="narrow-page page-stack">
      <div>
        <p className="eyebrow">WORKSPACE SETTINGS</p>
        <h1>Appearance</h1>
        <p className="page-intro">Set the RAVEN workspace to match your device or your desk.</p>
      </div>
      <Panel title="Appearance" eyebrow="PREFERENCES">
        <ThemeSelector />
      </Panel>
      <Panel title="AI & research" eyebrow="PROVIDER STATUS">
        <dl className="definition-list">
          <div><dt>Brave Search</dt><dd>{hasProviderStatus ? status(providers!.brave.configured) : "Checking…"}</dd></div>
          <div><dt>Crawl4AI Local</dt><dd>{hasProviderStatus ? status(providers!.crawl4Ai.configured, providers!.crawl4Ai.available) : "Checking…"}</dd></div>
          <div><dt>Gemini</dt><dd>{hasProviderStatus ? status(providers!.gemini.configured) : "Checking…"}</dd></div>
          <div><dt>Fast Research Model</dt><dd>{hasProviderStatus ? providers!.gemini.selectedModel || "Gemini 3.5 Flash-Lite" : "Checking…"}</dd></div>
          <div><dt>Deep Research Model</dt><dd>{hasProviderStatus ? providers!.deepResearchModel : "Checking…"}</dd></div>
        </dl>
      </Panel>
      <Panel className="future-settings" title="Sound" eyebrow="COMING LATER">
        <p>RAVEN has no sound events to configure yet.</p>
      </Panel>
    </div>
  );
}
