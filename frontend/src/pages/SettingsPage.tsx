import { useEffect, useMemo, useState, type DragEvent } from "react";
import { Binoculars, Brain, Buildings, CaretDown, CaretRight, CaretUp, DotsSixVertical, GlobeHemisphereWest, Microphone, Minus, Sun, UserCircle, UsersThree } from "@phosphor-icons/react";
import { useTheme, type ThemePreference } from "../app/theme";
import { Button } from "../components/Button";
import { Panel } from "../components/Panel";
import { ThemeSelector } from "../components/ThemeSelector";
import { VoiceSpeechSettings } from "../features/ask-raven/VoiceSpeechSettings";
import { defaultSpeechPreferences, readSpeechPreferences, saveSpeechPreferences, type SpeechPreferences } from "../features/ask-raven/speechPreferences";
import { getProviderHealth, getProviderStatus, type ProviderHealthResponse, type ProviderStatusResponse } from "../api/system";
import {
  getGeminiModelCatalog,
  getResearchSettings,
  resetResearchSettings,
  updateResearchSettings,
  type GeminiModelOption,
  type ProviderPreset,
  type ResearchSettings,
  type UpdateResearchSettings,
} from "../api/settings";
import styles from "./settings.module.css";
import { ResearchBehaviorSettings } from "./settings/ResearchBehaviorSettings";
import { ModelSettings } from "./settings/ModelSettings";
import { ResearchProviderSettings, displayProvider } from "./settings/ResearchProviderSettings";
import { ManagedResearchSettings } from "./settings/ManagedResearchSettings";
import { AccountSettings } from "../features/auth/AccountSettings";
import { WorkspaceAccessSettings } from "../features/auth/WorkspaceAccessSettings";
import { useAuth } from "../features/auth/AuthProvider";

const fallbackSettings: ResearchSettings = {
  groundingMode: "Auto",
  profileModel: "gemini-3.5-flash-lite",
  chatModel: "gemini-3.5-flash-lite",
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


const fallbackModels: Array<{ value: string; label: string; description: string; availability: GeminiModelOption["availability"] }> = [
  { value: "gemini-3.5-flash-lite", label: "Gemini 3.5 Flash-Lite", description: "Fast · high-throughput · good for identity and interactive tasks", availability: "Unverified" },
  { value: "gemini-3.5-flash", label: "Gemini 3.5 Flash", description: "Balanced capability and throughput for structured tasks", availability: "Unverified" },
  { value: "gemini-3.8-flash", label: "Gemini 3.8 Flash", description: "Higher capability for synthesis and complex analysis", availability: "Unverified" },
];


const settingsSections = ["Research behavior", "AI & models", "Research providers", "Deep Research", "Voice & speech", "Appearance", "Account", "Workspace access"] as const;
type SettingsSection = typeof settingsSections[number];
const settingsSectionIcons = {
  "Research behavior": Buildings,
  "AI & models": Brain,
  "Research providers": GlobeHemisphereWest,
  "Deep Research": Binoculars,
  "Voice & speech": Microphone,
  Appearance: Sun,
  Account: UserCircle,
  "Workspace access": UsersThree,
} satisfies Record<SettingsSection, typeof Buildings>;

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
    chatModel: typeof candidate.chatModel === "string" ? candidate.chatModel : candidate.profileModel,
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


export function SettingsPage() {
  const { isAdmin } = useAuth();
  const { preference: currentThemePreference, setPreference: setThemePreference } = useTheme();
  const [savedSettings, setSavedSettings] = useState<ResearchSettings>(fallbackSettings);
  const [draft, setDraft] = useState<ResearchSettings>(fallbackSettings);
  const [savedSpeechPreferences, setSavedSpeechPreferences] = useState<SpeechPreferences>(readSpeechPreferences);
  const [speechDraft, setSpeechDraft] = useState<SpeechPreferences>(readSpeechPreferences);
  const [savedThemePreference, setSavedThemePreference] = useState<ThemePreference>(currentThemePreference);
  const [themeDraftPreference, setThemeDraftPreference] = useState<ThemePreference>(currentThemePreference);
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

  useEffect(() => {
    setSavedThemePreference(currentThemePreference);
    setThemeDraftPreference(currentThemePreference);
  }, [currentThemePreference]);

  const researchSettingsDirty = useMemo(() => !sameSettings(draft, savedSettings), [draft, savedSettings]);
  const speechPreferencesDirty = useMemo(() => JSON.stringify(speechDraft) !== JSON.stringify(savedSpeechPreferences), [speechDraft, savedSpeechPreferences]);
  const appearanceDirty = themeDraftPreference !== savedThemePreference;
  const isDirty = researchSettingsDirty || speechPreferencesDirty || appearanceDirty;

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
      if (researchSettingsDirty) {
        const { updatedAt: _updatedAt, ...settingsToSave } = draft;
        const updated = await updateResearchSettings(settingsToSave);
        setSavedSettings(updated);
        setDraft(updated);
      }
      if (speechPreferencesDirty) {
        saveSpeechPreferences(speechDraft);
        setSavedSpeechPreferences(speechDraft);
      }
      if (appearanceDirty) {
        setThemePreference(themeDraftPreference);
        setSavedThemePreference(themeDraftPreference);
      }
      setStatusMessage("Settings saved.");
    } catch {
      setError("RAVEN could not save these settings. Check that the API is running and try again.");
    } finally {
      setSaving(false);
    }
  };

  const discard = () => {
    setDraft(savedSettings);
    setSpeechDraft(savedSpeechPreferences);
    setThemeDraftPreference(savedThemePreference);
    setError(null);
    setStatusMessage("Unsaved changes discarded.");
  };

  const reset = async () => {
    if (activeSection === "Voice & speech") {
      setSpeechDraft({ ...defaultSpeechPreferences });
      setError(null);
      setStatusMessage("Default Voice & speech preferences selected. Save changes to apply them.");
      return;
    }
    if (activeSection === "Appearance") {
      setThemeDraftPreference("system");
      setError(null);
      setStatusMessage("System appearance selected. Save changes to apply it.");
      return;
    }
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
      <div className={styles.customEditorHeading}><strong>{label}</strong><small>The first available provider is used.</small></div>
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
  const configuredGeminiModels = new Set([
    savedSettings.groundingModel,
    savedSettings.profileModel,
    savedSettings.chatModel,
    savedSettings.deepResearchModel,
  ]);
  const recentGeminiIssue = (providerHealth?.models ?? [])
    .filter((item) => item.provider.toLowerCase().includes("gemini") && item.state === "Degraded"
      && (item.model == null || configuredGeminiModels.has(item.model)))
    .sort((left, right) => Date.parse(right.lastFailureAt ?? "") - Date.parse(left.lastFailureAt ?? ""))[0];

  return (
    <div className={`page-stack ${styles.page}`}>
      <header className={styles.pageHeader}>
        <div><p className="eyebrow">WORKSPACE SETTINGS</p><h1>Settings</h1><p className={`page-intro ${styles.intro}`}>Control how RAVEN identifies companies, uses AI and routes research.</p></div>
      </header>

      <div className={styles.summaryGrid} aria-label="Current research configuration">
        <div><small>AI provider</small><strong>Gemini</strong><span>4 independent model roles</span></div>
        <div><small>Research route</small><strong>{draft.providerPreset === "Resilient" ? "Balanced & resilient" : draft.providerPreset === "LocalFirst" ? "Local-first" : draft.providerPreset === "Cloud" ? "Cloud-first" : "Custom"}</strong><span>{displayProvider(draft.searchProviderPriority[0] ?? "brave")} search · {displayProvider(draft.crawlerProviderPriority[0] ?? "crawl4ai-local")} acquisition</span></div>
        <div><small>Deep Research</small><strong>Exa Agent</strong><span>{draft.managedResearchDepth} depth</span></div>
      </div>

      {recentGeminiIssue ? <aside className={styles.providerWarning} role="status"><div><strong>Gemini recently had a request failure{recentGeminiIssue.lastFailureHttpStatus === 429 ? " (rate limited)" : ""}.</strong><span>{recentGeminiIssue.model ?? "AI model"}{recentGeminiIssue.lastFailureAt ? ` · ${new Date(recentGeminiIssue.lastFailureAt).toLocaleTimeString()}` : ""}. Review Status for affected tasks and recent provider activity.</span></div><a href="/status">View System Status</a></aside> : null}

      <div className={styles.settingsLayout}>
        <nav className={styles.navigation} aria-label="Settings sections">
          {settingsSections.filter(section => section !== "Workspace access" || isAdmin).map(section => {
            const Icon = settingsSectionIcons[section];
            return <button type="button" key={section} aria-current={activeSection === section ? "page" : undefined} onClick={() => setActiveSection(section)}><Icon size={17} weight="bold" aria-hidden="true" /><span>{section}</span></button>;
          })}
        </nav>

        {(activeSection === "Account" || activeSection === "Workspace access") && isDirty ? <aside className={styles.pendingDraft} role="status">
          <span>Research or appearance settings have unsaved changes.</span>
          <div><Button type="button" tone="quiet" onClick={discard} disabled={isSaving || isResetting}>Discard draft</Button><Button type="button" onClick={() => void save()} loading={isSaving} disabled={isResetting}>Save draft</Button></div>
        </aside> : null}

        {activeSection === "Account" ? <AccountSettings /> : activeSection === "Workspace access" ? <WorkspaceAccessSettings /> : <form onSubmit={(event) => { event.preventDefault(); void save(); }}>
        <div className={styles.sectionContent}>
{activeSection === "Research behavior" ? <ResearchBehaviorSettings draft={draft} disabled={isLoading || isSaving || isResetting} updateDraft={updateDraft} /> : null}

{activeSection === "AI & models" ? <ModelSettings draft={draft} disabled={isLoading || isSaving || isResetting} updateDraft={updateDraft} roleOptions={roleOptions} selectedModel={selectedModel} modelAvailabilityVerified={modelAvailabilityVerified} modelAvailabilityMessage={modelAvailabilityMessage} noCompatibleModelsAvailable={noCompatibleModelsAvailable} /> : null}

{activeSection === "Research providers" ? <ResearchProviderSettings draft={draft} disabled={isLoading || isSaving || isResetting} providers={providers} selectPreset={selectPreset} customPriorityEditor={customPriorityEditor} /> : null}

{activeSection === "Deep Research" ? <ManagedResearchSettings draft={draft} disabled={isLoading || isSaving || isResetting} providers={providers} updateDraft={updateDraft} /> : null}

          {activeSection === "Voice & speech" ? <Panel title="Voice & speech" eyebrow="PREFERENCES" className={styles.section}>
            <div className={styles.sectionIntro}><p>Choose how Ask RAVEN listens and reads responses aloud on this browser.</p></div>
            <VoiceSpeechSettings preferences={speechDraft} onChange={setSpeechDraft} geminiConfigured={!!providers?.gemini?.configured} disabled={isSaving || isResetting} />
          </Panel> : null}

          {activeSection === "Appearance" ? <Panel title="Appearance" eyebrow="PREFERENCES" className={styles.section}><ThemeSelector preference={themeDraftPreference} onChange={setThemeDraftPreference} disabled={isSaving || isResetting} /></Panel> : null}

        </div>

        {error && <p className={styles.error} role="alert">{error}</p>}
        {isLoading && <p className={styles.loading} role="status">Loading saved settings…</p>}
        {statusMessage && !isLoading && <p className={`${styles.state} ${styles["state--success"]}`} role="status" aria-live="polite">{statusMessage}</p>}
        <div className={styles.actions}><p className={`${styles.state} ${isDirty ? styles["state--dirty"] : ""}`} aria-live="polite">{isDirty ? "You have unsaved changes." : "All settings saved."}</p><div className={styles.actionGroup}><Button type="button" tone="quiet" onClick={discard} disabled={!isDirty || isSaving || isResetting}>Discard</Button><Button type="button" tone="secondary" onClick={() => void reset()} loading={isResetting} disabled={isSaving || isLoading}>Reset defaults</Button><Button type="submit" loading={isSaving} disabled={!isDirty || isResetting || isLoading}>Save changes</Button></div></div>
        </form>}
      </div>
    </div>
  );
}
