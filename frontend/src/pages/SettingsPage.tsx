import { useEffect, useMemo, useState, type DragEvent } from "react";
import { CaretDown, CaretUp, DotsSixVertical, Minus } from "@phosphor-icons/react";
import { Button } from "../components/Button";
import { Panel } from "../components/Panel";
import { ThemeSelector } from "../components/ThemeSelector";
import { getProviderHealth, getProviderStatus, type ProviderHealthResponse, type ProviderStatus, type ProviderStatusResponse } from "../api/system";
import {
  getGeminiModelCatalog,
  getResearchSettings,
  resetResearchSettings,
  updateResearchSettings,
  type GroundingMode,
  type ManagedResearchDepth,
  type GeminiModelOption,
  type ProviderPreset,
  type ResearchSettings,
  type UpdateResearchSettings,
} from "../api/settings";
import styles from "./settings.module.css";

const fallbackSettings: ResearchSettings = {
  groundingMode: "Auto",
  profileModel: "gemini-3.5-flash-lite",
  groundingModel: "gemini-3.5-flash-lite",
  deepResearchModel: "gemini-3.8-flash",
  aiSourceRerankingEnabled: true,
  providerPreset: "LocalFirst",
  searchProviderPriority: ["brave"],
  crawlerProviderPriority: ["crawl4ai-local"],
  customSearchProviderPriority: ["brave", "exa"],
  customCrawlerProviderPriority: ["crawl4ai-local", "exa"],
  managedResearchProvider: "exa-agent",
  managedResearchDepth: "Adaptive",
  updatedAt: "",
};

const managedResearchDepthChoices: Array<{ value: ManagedResearchDepth; title: string; description: string }> = [
  { value: "Adaptive", title: "Adaptive", description: "Let managed research choose an appropriate effort for the question." },
  { value: "Focused", title: "Focused", description: "A compact investigation for a narrow target." },
  { value: "Standard", title: "Standard", description: "Balanced breadth and depth for most investigations." },
  { value: "Thorough", title: "Thorough", description: "Broader source coverage for consequential questions." },
  { value: "Exhaustive", title: "Exhaustive", description: "The deepest available investigation; use selectively." },
];

const fallbackModels: Array<{ value: string; label: string; description: string; availability: GeminiModelOption["availability"] }> = [
  { value: "gemini-3.5-flash-lite", label: "Gemini 3.5 Flash-Lite", description: "Fast · high-throughput · good for identity and interactive tasks", availability: "Unverified" },
  { value: "gemini-3.5-flash", label: "Gemini 3.5 Flash", description: "Balanced capability and throughput for structured tasks", availability: "Unverified" },
  { value: "gemini-3.8-flash", label: "Gemini 3.8 Flash", description: "Higher capability for synthesis and complex analysis", availability: "Unverified" },
];

const groundingChoices: Array<{ value: GroundingMode; title: string; description: string }> = [
  { value: "Auto", title: "Smart matching · Recommended", description: "Use AI only when the company identity is ambiguous." },
  { value: "Always", title: "Always verify", description: "Ask AI to verify every research target before searching." },
  { value: "Off", title: "Deterministic only", description: "Skip AI matching and use the supplied identity directly." },
];

const presetChoices: Array<{ value: ProviderPreset; title: string; description: string }> = [
  { value: "Resilient", title: "Balanced & resilient · Recommended", description: "Local and low-cost providers first, with cloud fallback for transient failures." },
  { value: "LocalFirst", title: "Local-first", description: "Brave Search and local Crawl4AI only; lowest external usage." },
  { value: "Cloud", title: "Cloud-first", description: "Exa Search and hosted page acquisition." },
  { value: "Custom", title: "Custom", description: "Restore your saved custom route and choose its provider order in Advanced routing." },
];

const settingsSections = ["Research behavior", "AI & models", "Research providers", "Deep Research", "Appearance", "Advanced"] as const;
type SettingsSection = typeof settingsSections[number];

const presetPriorities: Record<Exclude<ProviderPreset, "Custom">, Pick<UpdateResearchSettings, "searchProviderPriority" | "crawlerProviderPriority">> = {
  Resilient: {
    searchProviderPriority: ["brave", "exa"],
    crawlerProviderPriority: ["crawl4ai-local", "exa"],
  },
  LocalFirst: {
    searchProviderPriority: ["brave"],
    crawlerProviderPriority: ["crawl4ai-local"],
  },
  Cloud: {
    searchProviderPriority: ["exa"],
    crawlerProviderPriority: ["exa"],
  },
};

const customSearchProviders = ["brave", "exa"];
const customCrawlerProviders = ["crawl4ai-local", "exa"];

function sameSettings(left: ResearchSettings, right: ResearchSettings) {
  return JSON.stringify({ ...left, updatedAt: "" }) === JSON.stringify({ ...right, updatedAt: "" });
}

function parseResearchSettings(value: unknown): ResearchSettings | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const candidate = value as Partial<ResearchSettings>;
  if (
    (candidate.groundingMode !== "Auto" && candidate.groundingMode !== "Always" && candidate.groundingMode !== "Off")
    || typeof candidate.profileModel !== "string"
    || typeof candidate.groundingModel !== "string"
    || typeof candidate.deepResearchModel !== "string"
    || typeof candidate.aiSourceRerankingEnabled !== "boolean"
    || (candidate.providerPreset !== "Resilient" && candidate.providerPreset !== "LocalFirst" && candidate.providerPreset !== "Cloud" && candidate.providerPreset !== "Custom")
    || !Array.isArray(candidate.searchProviderPriority)
    || !Array.isArray(candidate.crawlerProviderPriority)
  ) return null;

  const managedResearchDepth = candidate.managedResearchDepth;
  const validManagedResearchDepth = managedResearchDepth === "Adaptive" || managedResearchDepth === "Focused" || managedResearchDepth === "Standard" || managedResearchDepth === "Thorough" || managedResearchDepth === "Exhaustive";
  const searchProviderPriority = candidate.searchProviderPriority.filter((provider): provider is string => typeof provider === "string");
  const crawlerProviderPriority = candidate.crawlerProviderPriority.filter((provider): provider is string => typeof provider === "string");

  return {
    groundingMode: candidate.groundingMode,
    profileModel: candidate.profileModel,
    groundingModel: candidate.groundingModel,
    deepResearchModel: candidate.deepResearchModel,
    aiSourceRerankingEnabled: candidate.aiSourceRerankingEnabled,
    providerPreset: candidate.providerPreset,
    searchProviderPriority,
    crawlerProviderPriority,
    customSearchProviderPriority: Array.isArray(candidate.customSearchProviderPriority)
      ? candidate.customSearchProviderPriority.filter((provider): provider is string => typeof provider === "string")
      : candidate.providerPreset === "Custom" ? searchProviderPriority : ["brave", "exa"],
    customCrawlerProviderPriority: Array.isArray(candidate.customCrawlerProviderPriority)
      ? candidate.customCrawlerProviderPriority.filter((provider): provider is string => typeof provider === "string")
      : candidate.providerPreset === "Custom" ? crawlerProviderPriority : ["crawl4ai-local", "exa"],
    managedResearchProvider: typeof candidate.managedResearchProvider === "string" ? candidate.managedResearchProvider : "exa-agent",
    managedResearchDepth: validManagedResearchDepth ? managedResearchDepth : "Adaptive",
    updatedAt: typeof candidate.updatedAt === "string" ? candidate.updatedAt : "",
  };
}

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

function displayProvider(value: string) {
  const labels: Record<string, string> = {
    brave: "Brave Search",
    exa: "Exa Search & Contents",
    "crawl4ai-local": "Crawl4AI Local",
    "crawl4ai-cloud": "Crawl4AI Cloud",
  };
  return labels[value] ?? value;
}

export function SettingsPage() {
  const [savedSettings, setSavedSettings] = useState<ResearchSettings>(fallbackSettings);
  const [draft, setDraft] = useState<ResearchSettings>(fallbackSettings);
  const [providers, setProviders] = useState<ProviderStatusResponse | null>(null);
  const [providerHealth, setProviderHealth] = useState<ProviderHealthResponse | null>(null);
  const [modelOptions, setModelOptions] = useState(fallbackModels);
  const [modelAvailabilityVerified, setModelAvailabilityVerified] = useState(false);
  const [modelAvailabilityMessage, setModelAvailabilityMessage] = useState("");
  const [activeSection, setActiveSection] = useState<SettingsSection>("Research behavior");
  const [isLoading, setLoading] = useState(true);
  const [isSaving, setSaving] = useState(false);
  const [isResetting, setResetting] = useState(false);
  const [draggingPriority, setDraggingPriority] = useState<{ kind: "searchProviderPriority" | "crawlerProviderPriority"; index: number } | null>(null);
  const [dragOverPriority, setDragOverPriority] = useState<{ kind: "searchProviderPriority" | "crawlerProviderPriority"; index: number } | null>(null);
  const [movedPriority, setMovedPriority] = useState<{ kind: "searchProviderPriority" | "crawlerProviderPriority"; index: number } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [statusMessage, setStatusMessage] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    setLoading(true);
    void getGeminiModelCatalog().then(catalog => {
      if (!active) return;
      setModelAvailabilityVerified(catalog.projectAvailabilityVerified);
      setModelAvailabilityMessage(catalog.message);
      setModelOptions(catalog.models.map(model => ({ value: model.id, label: model.displayName, description: model.description, availability: model.availability })));
    }).catch(() => undefined);
    Promise.allSettled([getResearchSettings(), getProviderStatus(), getProviderHealth()]).then(([settingsResult, providerResult, healthResult]) => {
      if (!active) return;

      if (settingsResult.status === "fulfilled") {
        const loadedSettings = parseResearchSettings(settingsResult.value);
        if (loadedSettings) {
          setSavedSettings(loadedSettings);
          setDraft(loadedSettings);
        } else {
          setError("RAVEN returned an invalid research settings response. Defaults are shown until the API is updated.");
        }
      } else {
        setError("RAVEN could not load persistent research settings. Defaults are shown until the API is available.");
      }

      if (providerResult.status === "fulfilled") {
        setProviders(providerResult.value);
      }
      if (healthResult.status === "fulfilled") setProviderHealth(healthResult.value);
      setLoading(false);
    });

    return () => { active = false; };
  }, []);

  const isDirty = useMemo(() => !sameSettings(draft, savedSettings), [draft, savedSettings]);

  const updateDraft = (changes: Partial<ResearchSettings>) => {
    setDraft((current) => ({ ...current, ...changes }));
    setError(null);
    setStatusMessage(null);
  };

  const save = async () => {
    setSaving(true);
    setError(null);
    setStatusMessage(null);
    try {
      const { updatedAt: _updatedAt, ...settingsToSave } = draft;
      const updated = await updateResearchSettings(settingsToSave);
      setSavedSettings(updated);
      setDraft(updated);
      setStatusMessage("Research settings saved.");
    } catch {
      setError("RAVEN could not save these settings. Check that the API is running and try again.");
    } finally {
      setSaving(false);
    }
  };

  const discard = () => {
    setDraft(savedSettings);
    setError(null);
    setStatusMessage("Unsaved changes discarded.");
  };

  const reset = async () => {
    setResetting(true);
    setError(null);
    setStatusMessage(null);
    try {
      const defaults = await resetResearchSettings();
      setSavedSettings(defaults);
      setDraft(defaults);
      setStatusMessage("Research settings reset to defaults.");
    } catch {
      setError("RAVEN could not reset research settings. Check that the API is running and try again.");
    } finally {
      setResetting(false);
    }
  };

  const selectPreset = (preset: ProviderPreset) => {
    if (preset === "Custom") {
      updateDraft({
        providerPreset: preset,
        searchProviderPriority: [...draft.customSearchProviderPriority],
        crawlerProviderPriority: [...draft.customCrawlerProviderPriority],
      });
      return;
    }
    updateDraft({ providerPreset: preset, ...presetPriorities[preset] });
  };

  const updatePriority = (kind: "searchProviderPriority" | "crawlerProviderPriority", next: string[]) => {
    updateDraft({
      providerPreset: "Custom",
      [kind]: next,
      [kind === "searchProviderPriority" ? "customSearchProviderPriority" : "customCrawlerProviderPriority"]: [...next],
    } as Partial<ResearchSettings>);
  };

  const toggleCustomProvider = (kind: "searchProviderPriority" | "crawlerProviderPriority", provider: string) => {
    const current = draft[kind];
    if (current.includes(provider)) {
      if (current.length === 1) return;
      updatePriority(kind, current.filter((item) => item !== provider));
      return;
    }
    updatePriority(kind, [...current, provider]);
  };

  const moveCustomProvider = (kind: "searchProviderPriority" | "crawlerProviderPriority", index: number, offset: -1 | 1) => {
    const nextIndex = index + offset;
    const current = [...draft[kind]];
    if (nextIndex < 0 || nextIndex >= current.length) return;
    [current[index], current[nextIndex]] = [current[nextIndex], current[index]];
    updatePriority(kind, current);
    setMovedPriority({ kind, index: nextIndex });
    window.setTimeout(() => setMovedPriority(null), 600);
  };

  const dragStartCustomProvider = (event: DragEvent<HTMLLIElement>, kind: "searchProviderPriority" | "crawlerProviderPriority", index: number) => {
    setDraggingPriority({ kind, index });
    setDragOverPriority({ kind, index });
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("text/plain", `${kind}:${index}`);
  };

  const dropCustomProvider = (event: DragEvent<HTMLLIElement>, kind: "searchProviderPriority" | "crawlerProviderPriority", targetIndex: number) => {
    event.preventDefault();
    if (!draggingPriority || draggingPriority.kind !== kind || draggingPriority.index === targetIndex) {
      setDraggingPriority(null);
      setDragOverPriority(null);
      return;
    }
    const current = [...draft[kind]];
    const [moved] = current.splice(draggingPriority.index, 1);
    if (moved) current.splice(targetIndex, 0, moved);
    updatePriority(kind, current);
    setMovedPriority({ kind, index: targetIndex });
    window.setTimeout(() => setMovedPriority(null), 600);
    setDraggingPriority(null);
    setDragOverPriority(null);
  };

  const customPriorityEditor = (kind: "searchProviderPriority" | "crawlerProviderPriority", label: string, options: string[]) => {
    const values = draft[kind];
    return <div className={styles.customEditor}>
      <div className={styles.customEditorHeading}><strong>{label}</strong><small>First available provider wins.</small></div>
      <div className={styles.customEditorBody}>
        <div className={styles.customProviderChoices}>
          {options.map((provider) => <label key={provider} className={styles.customProviderChoice}><input type="checkbox" checked={values.includes(provider)} onChange={() => toggleCustomProvider(kind, provider)} /><span>{displayProvider(provider)}</span></label>)}
        </div>
        <ol className={styles.customPriorityList} aria-label={`${label} order`}>
          {values.map((provider, index) => <li
          key={provider}
          draggable
          data-dragging={draggingPriority?.kind === kind && draggingPriority.index === index ? "true" : undefined}
          data-drop-target={dragOverPriority?.kind === kind && dragOverPriority.index === index && draggingPriority?.index !== index ? "true" : undefined}
          data-moved={movedPriority?.kind === kind && movedPriority.index === index ? "true" : undefined}
          onDragStart={(event) => dragStartCustomProvider(event, kind, index)}
          onDragOver={(event) => { event.preventDefault(); setDragOverPriority({ kind, index }); }}
          onDrop={(event) => dropCustomProvider(event, kind, index)}
          onDragEnd={() => { setDraggingPriority(null); setDragOverPriority(null); }}
          aria-label={`${displayProvider(provider)}, priority ${index + 1}`}
          >
            <span className={styles.customPriorityDragHandle} title="Drag to reorder"><DotsSixVertical size={16} weight="bold" aria-hidden="true" /></span>
            <span className={styles.customPriorityNumber}>{index + 1}</span>
            <strong>{displayProvider(provider)}</strong>
            {index === 0 ? <span className={styles.customPriorityBoundary} title="Already first in this order"><Minus size={14} weight="bold" aria-hidden="true" /></span> : <button type="button" className={styles.customPriorityButton} onClick={() => moveCustomProvider(kind, index, -1)} aria-label={`Move ${displayProvider(provider)} up`} title={`Move ${displayProvider(provider)} up`}><CaretUp size={15} weight="bold" aria-hidden="true" /></button>}
            {index === values.length - 1 ? <span className={styles.customPriorityBoundary} title="Already last in this order"><Minus size={14} weight="bold" aria-hidden="true" /></span> : <button type="button" className={styles.customPriorityButton} onClick={() => moveCustomProvider(kind, index, 1)} aria-label={`Move ${displayProvider(provider)} down`} title={`Move ${displayProvider(provider)} down`}><CaretDown size={15} weight="bold" aria-hidden="true" /></button>}
          </li>)}
        </ol>
      </div>
    </div>;
  };

  const roleOptions = (value: string) => modelOptions.some((model) => model.value === value)
    ? modelOptions
    : [{ value, label: value, description: "", availability: "Unverified" as const }, ...modelOptions];

  const selectedModel = (value: string) => modelOptions.find((model) => model.value === value);
  const noCompatibleModelsAvailable = modelAvailabilityVerified && modelOptions.length > 0 && modelOptions.every((model) => model.availability === "Unavailable");
  const recentGeminiIssue = (providerHealth?.models ?? [])
    .filter((item) => item.provider.toLowerCase().includes("gemini") && item.state === "Degraded")
    .sort((left, right) => Date.parse(right.lastFailureAt ?? "") - Date.parse(left.lastFailureAt ?? ""))[0];

  return (
    <div className={`page-stack ${styles.page}`}>
      <header className={styles.pageHeader}>
        <div><p className="eyebrow">WORKSPACE SETTINGS</p><h1>Settings</h1><p className={`page-intro ${styles.intro}`}>Control how RAVEN identifies companies, uses AI and routes research.</p></div>
      </header>

      <div className={styles.summaryGrid} aria-label="Current research configuration">
        <div><small>AI provider</small><strong>Gemini</strong><span>3 model settings across 4 task areas</span></div>
        <div><small>Research route</small><strong>{draft.providerPreset === "Resilient" ? "Balanced & resilient" : draft.providerPreset === "LocalFirst" ? "Local-first" : draft.providerPreset === "Cloud" ? "Cloud-first" : "Custom"}</strong><span>{displayProvider(draft.searchProviderPriority[0] ?? "brave")} search · {displayProvider(draft.crawlerProviderPriority[0] ?? "crawl4ai-local")} acquisition</span></div>
        <div><small>Deep Research</small><strong>Exa Agent</strong><span>{draft.managedResearchDepth} depth</span></div>
      </div>

      {recentGeminiIssue ? <aside className={styles.providerWarning} role="status"><div><strong>Gemini recently had a request failure{recentGeminiIssue.lastFailureHttpStatus === 429 ? " (rate limited)" : ""}.</strong><span>{recentGeminiIssue.model ?? "AI model"}{recentGeminiIssue.lastFailureAt ? ` · ${new Date(recentGeminiIssue.lastFailureAt).toLocaleTimeString()}` : ""}. Review Status for affected tasks and recent provider activity.</span></div><a href="/status">View System Status</a></aside> : null}

      <div className={styles.settingsLayout}>
        <nav className={styles.navigation} aria-label="Settings sections">
          {settingsSections.map(section => <button type="button" key={section} aria-current={activeSection === section ? "page" : undefined} onClick={() => setActiveSection(section)}>{section}</button>)}
        </nav>

        <form onSubmit={(event) => { event.preventDefault(); void save(); }}>
        <div className={styles.sectionContent}>
          {activeSection === "Research behavior" ? <Panel title="Company matching" eyebrow="RESEARCH BEHAVIOR" className={styles.section}>
            <div className={styles.sectionIntro}><p>How carefully should RAVEN verify which company you mean before searching?</p></div>
            <fieldset className={styles.fieldSet} disabled={isLoading || isSaving || isResetting}>
              <legend>Matching behavior</legend><p className={styles.fieldHint}>This default applies to new research. A research run can override it for that request.</p>
              <div className={styles.choiceGrid}>{groundingChoices.map(choice => <label className={styles.choice} key={choice.value}><input type="radio" name="grounding-mode" value={choice.value} checked={draft.groundingMode === choice.value} onChange={() => updateDraft({ groundingMode: choice.value })} /><span className={styles.choiceCopy}><strong>{choice.title}</strong><small>{choice.description}</small></span></label>)}</div>
            </fieldset>
            <label className={styles.toggleRow}><span className={styles.toggleCopy}><strong>AI-assisted source ranking</strong><span>Prioritize sources that better match the resolved company identity.</span></span><input className={styles.toggle} type="checkbox" role="switch" checked={draft.aiSourceRerankingEnabled} disabled={isLoading || isSaving || isResetting} onChange={event => updateDraft({ aiSourceRerankingEnabled: event.target.checked })} aria-label="AI-assisted source ranking" /></label>
          </Panel> : null}

          {activeSection === "AI & models" ? <Panel title="AI models" eyebrow="GEMINI · RAVEN TASKS" className={styles.section}>
            <div className={styles.sectionIntro}><p>Choose from RAVEN-tested Gemini models that support the structured outputs used by these tasks. {modelAvailabilityVerified ? noCompatibleModelsAvailable ? "Project access was checked." : modelAvailabilityMessage : modelAvailabilityMessage || "Project availability is unverified; RAVEN's compatible choices remain available."}</p></div>
            {noCompatibleModelsAvailable ? <div className={styles.modelWarning} role="alert"><strong>No RAVEN-supported Gemini models were listed for this API key/project.</strong><span>Check that GEMINI_API_KEY belongs to the intended Google AI project, Gemini API access is enabled, and the key is allowed to use the Gemini API. RAVEN does not send a generation request to check this.</span></div> : null}
            <div className={styles.modelGrid}>
              <div className={styles.modelRole}><label htmlFor="grounding-model">Company matching</label><small>Resolves ambiguous identity and ranks relevant sources.</small><select id="grounding-model" value={draft.groundingModel} disabled={isLoading || isSaving || isResetting} onChange={event => updateDraft({ groundingModel: event.target.value })}>{roleOptions(draft.groundingModel).map(model => <option key={model.value} value={model.value} disabled={model.availability === "Unavailable"}>{model.label}{model.availability === "Unavailable" ? " · unavailable to this project" : ""}</option>)}</select><small>{selectedModel(draft.groundingModel)?.description}{selectedModel(draft.groundingModel)?.availability === "Unavailable" ? " Not available to the configured project; choose another model." : ""}</small></div>
              <div className={styles.modelRole}><label htmlFor="profile-model">Company Profile & Ask RAVEN</label><small>Generates evidence-backed Profiles and answers company questions. These tasks currently share one model setting.</small><select id="profile-model" value={draft.profileModel} disabled={isLoading || isSaving || isResetting} onChange={event => updateDraft({ profileModel: event.target.value })}>{roleOptions(draft.profileModel).map(model => <option key={model.value} value={model.value} disabled={model.availability === "Unavailable"}>{model.label}{model.availability === "Unavailable" ? " · unavailable to this project" : ""}</option>)}</select><small>{selectedModel(draft.profileModel)?.description}{selectedModel(draft.profileModel)?.availability === "Unavailable" ? " Not available to the configured project; choose another model." : "Change this selection to change the model used by both Company Profile generation and Ask RAVEN."}</small></div>
              <div className={styles.modelRole}><label htmlFor="synthesis-model">Briefings & RAVEN analysis</label><small>Briefing synthesis, Investigation analysis and RAVEN-run Deep Research. This does not select the model behind Exa Agent.</small><select id="synthesis-model" value={draft.deepResearchModel} disabled={isLoading || isSaving || isResetting} onChange={event => updateDraft({ deepResearchModel: event.target.value })}>{roleOptions(draft.deepResearchModel).map(model => <option key={model.value} value={model.value} disabled={model.availability === "Unavailable"}>{model.label}{model.availability === "Unavailable" ? " · unavailable to this project" : ""}</option>)}</select><small>{selectedModel(draft.deepResearchModel)?.description}{selectedModel(draft.deepResearchModel)?.availability === "Unavailable" ? " Not available to the configured project; choose another model." : ""}</small></div>
            </div>
            <p className={styles.catalogNote}>RAVEN exposes only its compatibility-tested model catalog. Project discovery checks model access and GenerateContent support; structured-output compatibility is maintained by RAVEN. Exact quotas remain visible in Google AI Studio.</p>
          </Panel> : null}

          {activeSection === "Research providers" ? <Panel title="Research route" eyebrow="RESEARCH PROVIDERS" className={styles.section}>
            <div className={styles.sectionIntro}><p>Choose the provider route RAVEN should try. Cloud providers are not probed with billable requests from this page.</p></div>
            <fieldset className={styles.fieldSet} disabled={isLoading || isSaving || isResetting}><legend>Starting route</legend><div className={styles.presetGrid}>{presetChoices.map(choice => <label className={`${styles.preset} ${draft.providerPreset === choice.value ? styles["preset--active"] : ""}`} key={choice.value}><input className="sr-only" type="radio" name="provider-preset" value={choice.value} checked={draft.providerPreset === choice.value} onChange={() => selectPreset(choice.value)} /><strong>{choice.title}</strong><span>{choice.description}</span></label>)}</div></fieldset>
            <div className={styles.routePreview}><strong>Your route</strong><span><small>Search</small>{draft.searchProviderPriority.map(displayProvider).join(" → ")}</span><span><small>Read pages</small>{draft.crawlerProviderPriority.map(displayProvider).join(" → ")}</span></div>
            {draft.providerPreset === "Custom" ? <button type="button" className={styles.settingsLink} onClick={() => setActiveSection("Advanced")}>Configure exact provider order in Advanced routing →</button> : null}
            <div className={styles.providerArea}><h3>Connection configuration</h3><div className={styles.providerGrid} aria-live="polite">{[["Brave Search", providers?.brave], ["Crawl4AI Local", providers?.crawl4Ai], ["Exa", providers?.exa], ["Gemini", providers?.gemini]].map(([name, provider]) => { const typedProvider = provider as ProviderStatus | undefined; return <div className={styles.providerStatus} key={name as string}><span className={`${styles.statusDot} ${providerStatusClass(typedProvider)}`} aria-hidden="true" /><span><strong>{name as string}</strong><small>{providerStatusLabel(typedProvider)}</small></span></div>; })}</div></div>
            <p className={styles.catalogNote}>For recent request health and rate limits, see <a href="/status">System Status</a>.</p>
          </Panel> : null}

          {activeSection === "Deep Research" ? <Panel title="Managed Deep Research" eyebrow="EXA AGENT" className={styles.section}>
            <div className={styles.sectionIntro}><p>Managed Deep Research runs asynchronously through Exa Agent and returns a reviewable Investigation. Exa controls its underlying model; Gemini model choices do not change Exa Agent. Managed research does not update the accepted Company Profile automatically.</p></div>
            <div className={styles.researchProvider}><span className={`${styles.statusDot} ${providers?.exa?.configured ? styles["statusDot--checking"] : styles["statusDot--warning"]}`} aria-hidden="true" /><div><strong>Exa Agent</strong><small>{providers?.exa?.configured ? "Configured · Exa chooses the model for managed multi-step research" : "Not configured · add an Exa API key to enable"}</small></div></div>
            <label className={styles.depthControl} htmlFor="managed-research-depth"><strong>Default research depth</strong><small>Choose the breadth of new managed Investigations.</small><select id="managed-research-depth" value={draft.managedResearchDepth} disabled={isLoading || isSaving || isResetting} onChange={event => updateDraft({ managedResearchDepth: event.target.value as ManagedResearchDepth })}>{managedResearchDepthChoices.map(choice => <option key={choice.value} value={choice.value}>{choice.title}</option>)}</select><small>{managedResearchDepthChoices.find(choice => choice.value === draft.managedResearchDepth)?.description}</small></label>
          </Panel> : null}

          {activeSection === "Appearance" ? <Panel title="Appearance" eyebrow="PREFERENCES" className={styles.section}><ThemeSelector /></Panel> : null}

          {activeSection === "Advanced" ? <Panel title="Advanced routing" eyebrow="TECHNICAL CONFIGURATION" className={styles.section}>
            {draft.providerPreset === "Custom" ? <div className={styles.customRouting}><div className={styles.customRoutingIntro}><strong>Custom provider order</strong><p>First available provider wins. Authentication and configuration failures are not silently retried.</p></div><div className={styles.customRoutingGrid}>{customPriorityEditor("searchProviderPriority", "Search order", customSearchProviders)}{customPriorityEditor("crawlerProviderPriority", "Page acquisition order", customCrawlerProviders)}</div><button type="button" className={styles.settingsLink} onClick={() => setActiveSection("Research providers")}>Back to Research providers →</button></div> : <div className={styles.sectionIntro}><p>Exact provider order is available when the Custom research route is selected.</p><button type="button" className={styles.settingsLink} onClick={() => setActiveSection("Research providers")}>Choose Custom under Research providers →</button><p>Model choices are restricted to RAVEN’s compatibility-tested catalog.</p></div>}
            <p className={styles.catalogNote}>The internal compatibility names remain GroundingMode and DeepResearchModel for existing workflow contracts.</p>
          </Panel> : null}
        </div>

        {error && <p className={styles.error} role="alert">{error}</p>}
        {isLoading && <p className={styles.loading} role="status">Loading saved settings…</p>}
        {statusMessage && !isLoading && <p className={`${styles.state} ${styles["state--success"]}`} role="status" aria-live="polite">{statusMessage}</p>}
        <div className={styles.actions}><p className={`${styles.state} ${isDirty ? styles["state--dirty"] : ""}`} aria-live="polite">{isDirty ? "You have unsaved changes." : "All settings saved."}</p><div className={styles.actionGroup}><Button type="button" tone="quiet" onClick={discard} disabled={!isDirty || isSaving || isResetting}>Discard</Button><Button type="button" tone="secondary" onClick={() => void reset()} loading={isResetting} disabled={isSaving}>Reset defaults</Button><Button type="submit" loading={isSaving} disabled={!isDirty || isResetting}>Save changes</Button></div></div>
        </form>
      </div>
    </div>
  );
}
