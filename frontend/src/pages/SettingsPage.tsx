import { useEffect, useMemo, useState, type DragEvent } from "react";
import { CaretDown, CaretUp, DotsSixVertical, Minus } from "@phosphor-icons/react";
import { Button } from "../components/Button";
import { Panel } from "../components/Panel";
import { ThemeSelector } from "../components/ThemeSelector";
import { getProviderStatus, type ProviderStatus, type ProviderStatusResponse } from "../api/system";
import {
  getResearchSettings,
  resetResearchSettings,
  updateResearchSettings,
  type GroundingMode,
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
  updatedAt: "",
};

const models = [
  { value: "gemini-3.5-flash-lite", label: "Gemini 3.5 Flash-Lite" },
  { value: "gemini-3.8-flash", label: "Gemini 3.8 Flash" },
];

const groundingChoices: Array<{ value: GroundingMode; title: string; description: string }> = [
  { value: "Auto", title: "Auto — Recommended", description: "Use AI when RAVEN detects an ambiguous company identity." },
  { value: "Always", title: "Always", description: "Resolve the research target with AI before full discovery." },
  { value: "Off", title: "Off", description: "Use deterministic Day-3 discovery and recommendations only." },
];

const presetChoices: Array<{ value: ProviderPreset; title: string; description: string }> = [
  { value: "Resilient", title: "RAVEN Resilient", description: "Use Brave and local Crawl4AI first, then retry with Exa or Firecrawl when a provider fails." },
  { value: "LocalFirst", title: "RAVEN Local First", description: "Use Brave Search and local Crawl4AI only for the lowest-cost, local-first route." },
  { value: "Cloud", title: "RAVEN Cloud", description: "Use Exa first, then Firecrawl, for both search and cloud retrieval." },
  { value: "Custom", title: "Custom", description: "Manually choose which providers are enabled and set their exact order below." },
];

const presetPriorities: Record<Exclude<ProviderPreset, "Custom">, Pick<UpdateResearchSettings, "searchProviderPriority" | "crawlerProviderPriority">> = {
  Resilient: {
    searchProviderPriority: ["brave", "exa", "firecrawl-search"],
    crawlerProviderPriority: ["crawl4ai-local", "exa", "firecrawl"],
  },
  LocalFirst: {
    searchProviderPriority: ["brave"],
    crawlerProviderPriority: ["crawl4ai-local"],
  },
  Cloud: {
    searchProviderPriority: ["exa", "firecrawl-search"],
    crawlerProviderPriority: ["exa", "firecrawl"],
  },
};

const customSearchProviders = ["brave", "exa", "firecrawl-search"];
const customCrawlerProviders = ["crawl4ai-local", "exa", "firecrawl"];

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

  return {
    groundingMode: candidate.groundingMode,
    profileModel: candidate.profileModel,
    groundingModel: candidate.groundingModel,
    deepResearchModel: candidate.deepResearchModel,
    aiSourceRerankingEnabled: candidate.aiSourceRerankingEnabled,
    providerPreset: candidate.providerPreset,
    searchProviderPriority: candidate.searchProviderPriority.filter((provider): provider is string => typeof provider === "string"),
    crawlerProviderPriority: candidate.crawlerProviderPriority.filter((provider): provider is string => typeof provider === "string"),
    updatedAt: typeof candidate.updatedAt === "string" ? candidate.updatedAt : "",
  };
}

function providerStatusLabel(provider: ProviderStatus | undefined) {
  if (!provider) return "Checking…";
  if (!provider.configured) return "Not configured";
  if (provider.available === false) return "Unavailable";
  return "Configured";
}

function providerStatusClass(provider: ProviderStatus | undefined) {
  if (!provider) return "";
  return provider.configured && provider.available !== false ? styles["statusDot--ok"] : styles["statusDot--warning"];
}

function displayProvider(value: string) {
  const labels: Record<string, string> = {
    brave: "Brave Search",
    exa: "Exa Search & Contents",
    "firecrawl-search": "Firecrawl Search",
    firecrawl: "Firecrawl",
    "crawl4ai-local": "Crawl4AI Local",
    "crawl4ai-cloud": "Crawl4AI Cloud",
  };
  return labels[value] ?? value;
}

export function SettingsPage() {
  const [savedSettings, setSavedSettings] = useState<ResearchSettings>(fallbackSettings);
  const [draft, setDraft] = useState<ResearchSettings>(fallbackSettings);
  const [providers, setProviders] = useState<ProviderStatusResponse | null>(null);
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
    Promise.allSettled([getResearchSettings(), getProviderStatus()]).then(([settingsResult, providerResult]) => {
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
      updateDraft({ providerPreset: preset });
      return;
    }
    updateDraft({ providerPreset: preset, ...presetPriorities[preset] });
  };

  const updatePriority = (kind: "searchProviderPriority" | "crawlerProviderPriority", next: string[]) => {
    updateDraft({ providerPreset: "Custom", [kind]: next } as Partial<ResearchSettings>);
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

  const roleOptions = (value: string) => models.some((model) => model.value === value)
    ? models
    : [{ value, label: value }, ...models];

  return (
    <div className={`narrow-page page-stack ${styles.page}`}>
      <div>
        <p className="eyebrow">WORKSPACE SETTINGS</p>
        <h1>Research intelligence</h1>
        <p className={`page-intro ${styles.intro}`}>Choose how RAVEN resolves company identity, prioritizes public sources, and assigns models to each research job.</p>
      </div>

      <form onSubmit={(event) => { event.preventDefault(); void save(); }}>
      <Panel title="AI-assisted research" eyebrow="RESEARCH INTELLIGENCE" className={styles.section}>
        <div className={styles.sectionIntro}>
          <p>Grounding helps RAVEN distinguish a parent group, subsidiary, brand, or same-name company before it spends time acquiring sources.</p>
        </div>
        <fieldset className={styles.fieldSet} disabled={isLoading || isSaving || isResetting}>
          <legend>Grounding default</legend>
          <p className={styles.fieldHint}>The default applies to new research runs. Research Company can override it for one run.</p>
          <div className={styles.choiceGrid}>
            {groundingChoices.map((choice) => (
              <label className={styles.choice} key={choice.value}>
                <input
                  type="radio"
                  name="grounding-mode"
                  value={choice.value}
                  checked={draft.groundingMode === choice.value}
                  onChange={() => updateDraft({ groundingMode: choice.value })}
                />
                <span className={styles.choiceCopy}>
                  <strong>{choice.title}</strong>
                  <small>{choice.description}</small>
                </span>
              </label>
            ))}
          </div>
        </fieldset>

        <label className={styles.toggleRow}>
          <span className={styles.toggleCopy}>
            <strong>AI source recommendations</strong>
            <span>Use the grounded target and search evidence to improve which sources are recommended by default.</span>
          </span>
          <input
            className={styles.toggle}
            type="checkbox"
            role="switch"
            checked={draft.aiSourceRerankingEnabled}
            disabled={isLoading || isSaving || isResetting}
            onChange={(event) => updateDraft({ aiSourceRerankingEnabled: event.target.checked })}
            aria-label="AI source recommendations"
          />
        </label>
      </Panel>

      <Panel title="Model roles" eyebrow="AI MODELS" className={styles.section}>
        <div className={styles.sectionIntro}>
          <p>Each model role is explicit. Changing one role does not silently change the others.</p>
        </div>
        <div className={styles.modelGrid}>
          <div className={styles.modelRole}>
            <label htmlFor="profile-model">Profile generation</label>
            <small>Structured Company Profile extraction from acquired evidence.</small>
            <select id="profile-model" value={draft.profileModel} disabled={isLoading || isSaving || isResetting} onChange={(event) => updateDraft({ profileModel: event.target.value })}>
              {roleOptions(draft.profileModel).map((model) => <option key={model.value} value={model.value}>{model.label}</option>)}
            </select>
          </div>
          <div className={styles.modelRole}>
            <label htmlFor="grounding-model">Identity grounding</label>
            <small>Company disambiguation and source relevance decisions.</small>
            <select id="grounding-model" value={draft.groundingModel} disabled={isLoading || isSaving || isResetting} onChange={(event) => updateDraft({ groundingModel: event.target.value })}>
              {roleOptions(draft.groundingModel).map((model) => <option key={model.value} value={model.value}>{model.label}</option>)}
            </select>
          </div>
          <div className={styles.modelRole}>
            <label htmlFor="deep-research-model">Deep research</label>
            <small>Multi-step tool-assisted investigation for longer questions.</small>
            <select id="deep-research-model" value={draft.deepResearchModel} disabled={isLoading || isSaving || isResetting} onChange={(event) => updateDraft({ deepResearchModel: event.target.value })}>
              {roleOptions(draft.deepResearchModel).map((model) => <option key={model.value} value={model.value}>{model.label}</option>)}
            </select>
          </div>
        </div>
      </Panel>

      <Panel title="Provider routing" eyebrow="PROVIDERS" className={styles.section}>
        <div className={styles.sectionIntro}>
          <p>Select a starting preset. RAVEN only falls back for temporary provider failures; authentication and configuration errors stay visible.</p>
        </div>
        <fieldset className={styles.fieldSet} disabled={isLoading || isSaving || isResetting}>
          <legend>Research preset</legend>
          <p className={styles.fieldHint}>These presets control provider priority and fallback behavior, not research depth. Grounding <strong>Auto</strong> is a separate identity-resolution setting.</p>
          <div className={styles.presetGrid}>
            {presetChoices.map((choice) => (
              <label className={`${styles.preset} ${draft.providerPreset === choice.value ? styles["preset--active"] : ""}`} key={choice.value}>
                <input className="sr-only" type="radio" name="provider-preset" value={choice.value} checked={draft.providerPreset === choice.value} onChange={() => selectPreset(choice.value)} />
                <strong>{choice.title}</strong>
                <span>{choice.description}</span>
              </label>
            ))}
          </div>
        </fieldset>

        <div className={styles.providerArea}>
          <h3>Current provider status</h3>
          <div className={styles.providerGrid} aria-live="polite">
            {[
              ["Brave Search", providers?.brave],
              ["Crawl4AI Local", providers?.crawl4Ai],
              ["Exa Search", providers?.exa],
              ["Firecrawl", providers?.firecrawl],
              ["Gemini", providers?.gemini],
            ].map(([name, provider]) => {
              const typedProvider = provider as ProviderStatus | undefined;
              return <div className={styles.providerStatus} key={name as string}>
                <span className={`${styles.statusDot} ${providerStatusClass(typedProvider)}`} aria-hidden="true" />
                <span><strong>{name as string}</strong><small>{providerStatusLabel(typedProvider)}</small></span>
              </div>;
            })}
          </div>
        </div>

        {draft.providerPreset !== "Custom" ? <div className={styles.priorityGrid}>
          <div>
            <span className={styles.priorityLabel}>Search priority</span>
            <div className={styles.priorityList} aria-label="Search provider priority">
              {draft.searchProviderPriority.map((provider) => <span className={styles.priorityItem} key={provider}>{displayProvider(provider)}</span>)}
            </div>
          </div>
          <div>
            <span className={styles.priorityLabel}>Crawler priority</span>
            <div className={styles.priorityList} aria-label="Crawler provider priority">
              {draft.crawlerProviderPriority.map((provider) => <span className={styles.priorityItem} key={provider}>{displayProvider(provider)}</span>)}
            </div>
          </div>
        </div> : null}

        {draft.providerPreset === "Custom" ? <div className={styles.customRouting}>
          <div className={styles.customRoutingIntro}><strong>Custom routing</strong><p>RAVEN will use the exact provider order below. This is manual routing, not a second cloud preset.</p></div>
          <div className={styles.customRoutingGrid}>
            {customPriorityEditor("searchProviderPriority", "Search order", customSearchProviders)}
            {customPriorityEditor("crawlerProviderPriority", "Crawler order", customCrawlerProviders)}
          </div>
        </div> : null}
      </Panel>

      <Panel title="Appearance" eyebrow="PREFERENCES" className={styles.section}>
        <ThemeSelector />
      </Panel>

        {error && <p className={styles.error} role="alert">{error}</p>}
        {isLoading && <p className={styles.loading} role="status">Loading persistent research settings…</p>}
        {statusMessage && !isLoading && <p className={`${styles.state} ${styles["state--success"]}`} role="status" aria-live="polite">{statusMessage}</p>}
        <div className={styles.actions}>
          <p className={`${styles.state} ${isDirty ? styles["state--dirty"] : ""}`} aria-live="polite">
            {isDirty ? "You have unsaved changes." : "All research settings saved."}
          </p>
          <div className={styles.actionGroup}>
            <Button type="button" tone="quiet" onClick={discard} disabled={!isDirty || isSaving || isResetting}>Discard</Button>
            <Button type="button" tone="secondary" onClick={() => void reset()} loading={isResetting} disabled={isSaving}>Reset defaults</Button>
            <Button type="submit" loading={isSaving} disabled={!isDirty || isResetting}>Save changes</Button>
          </div>
        </div>
      </form>
    </div>
  );
}
