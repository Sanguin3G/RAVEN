import { useEffect, useState } from "react";
import { Panel } from "../components/Panel";
import { ThemeSelector } from "../components/ThemeSelector";
import { getProviderStatus, updateModelPreferences, type ProviderStatusResponse } from "../api/system";

const models = [
  { value: "gemini-3.5-flash-lite", label: "Gemini 3.5 Flash-Lite" },
  { value: "gemini-3.8-flash", label: "Gemini 3.8 Flash" },
];

export function SettingsPage() {
  const [providers, setProviders] = useState<ProviderStatusResponse | null>(null);
  const [fastModel, setFastModel] = useState("gemini-3.5-flash-lite");
  const [deepModel, setDeepModel] = useState("gemini-3.8-flash");
  const [modelError, setModelError] = useState<string | null>(null);
  const [isSavingModel, setSavingModel] = useState(false);

  useEffect(() => {
    let active = true;
    getProviderStatus().then((result) => {
      if (!active) return;
      setProviders(result);
      setFastModel(result.gemini.selectedModel || "gemini-3.5-flash-lite");
      setDeepModel(result.deepResearchModel || "gemini-3.8-flash");
    }).catch(() => { if (active) setProviders(null); });
    return () => { active = false; };
  }, []);

  const status = (configured?: boolean, available?: boolean | null) => !configured ? "Not configured" : available === false ? "Unavailable" : "Configured";
  const hasProviderStatus = Boolean(providers?.brave && providers?.crawl4Ai && providers?.gemini);

  const saveModelPreferences = async (nextFast: string, nextDeep: string) => {
    setFastModel(nextFast);
    setDeepModel(nextDeep);
    setModelError(null);
    setSavingModel(true);
    try {
      const updated = await updateModelPreferences({ fastModel: nextFast, deepModel: nextDeep });
      setFastModel(updated.fastModel);
      setDeepModel(updated.deepModel);
      setProviders((current) => current ? {
        ...current,
        gemini: { ...current.gemini, selectedModel: updated.fastModel },
        deepResearchModel: updated.deepModel,
      } : current);
    } catch {
      setModelError("RAVEN could not save the runtime model choice. Check that the API is running.");
    } finally {
      setSavingModel(false);
    }
  };

  return (
    <div className="narrow-page page-stack">
      <div>
        <p className="eyebrow">WORKSPACE SETTINGS</p>
        <h1>Appearance & research</h1>
        <p className="page-intro">Set the RAVEN workspace, model defaults, and provider-aware research behavior.</p>
      </div>
      <Panel title="Appearance" eyebrow="PREFERENCES">
        <ThemeSelector />
      </Panel>
      <Panel title="AI & research" eyebrow="PROVIDER STATUS">
        <dl className="definition-list">
          <div><dt>Brave Search</dt><dd>{hasProviderStatus ? status(providers!.brave.configured) : "Checking…"}</dd></div>
          <div><dt>Crawl4AI Local</dt><dd>{hasProviderStatus ? status(providers!.crawl4Ai.configured, providers!.crawl4Ai.available) : "Checking…"}</dd></div>
          <div><dt>Gemini</dt><dd>{hasProviderStatus ? status(providers!.gemini.configured) : "Checking…"}</dd></div>
          <div><dt>Fast Research Model</dt><dd><label className="model-choice"><span className="sr-only">Fast Research Model</span><select disabled={!hasProviderStatus || isSavingModel} value={fastModel} onChange={(event) => void saveModelPreferences(event.target.value, deepModel)}>{models.map((model) => <option key={model.value} value={model.value}>{model.label}</option>)}</select></label></dd></div>
          <div><dt>Deep Research Model</dt><dd><label className="model-choice"><span className="sr-only">Deep Research Model</span><select disabled={!hasProviderStatus || isSavingModel} value={deepModel} onChange={(event) => void saveModelPreferences(fastModel, event.target.value)}>{models.map((model) => <option key={model.value} value={model.value}>{model.label}</option>)}</select></label></dd></div>
        </dl>
        <p className="settings-runtime-note">{isSavingModel ? "Saving model preference…" : "Applies to this local API runtime and resets to deployment defaults when the API restarts."}</p>
        {modelError && <p className="form-error">{modelError}</p>}
      </Panel>
      <Panel className="future-settings" title="Sound" eyebrow="COMING LATER">
        <p>RAVEN has no sound events to configure yet.</p>
      </Panel>
    </div>
  );
}
