import { useEffect, useState } from "react";
import { Panel } from "../../components/Panel";
import {
  getProviderCredentials,
  removeProviderCredential,
  replaceProviderCredential,
  testProviderCredential,
  type ProviderCredentialName,
  type ProviderCredentialStatus,
} from "../../api/providerCredentials";
import styles from "./provider-credentials.module.css";

const providers: Array<{ id: ProviderCredentialName; name: string; description: string }> = [
  { id: "brave", name: "Brave Search", description: "Search API key used for web discovery." },
  { id: "exa", name: "Exa", description: "Shared key for Exa Search, Contents, and Agent research." },
  { id: "gemini", name: "Gemini", description: "Shared key for generation, model checks, speech, and Deep Research chat." },
  { id: "google-maps", name: "Google Maps", description: "Browser-visible embed key. Restrict the Maps Embed API and allowed RAVEN origins/referrers in Google Cloud." },
  { id: "crawl4ai", name: "Crawl4AI", description: "Endpoint and API token for the configured Crawl4AI service." },
];

function sourceLabel(status: ProviderCredentialStatus | undefined) {
  if (!status?.configured) return "Not configured";
  if (status.source === "workspace") return "Workspace override";
  if (status.source === "environment") return "Deployment configuration";
  return "Not configured";
}

export function ProviderCredentialsSettings() {
  const [statuses, setStatuses] = useState<ProviderCredentialStatus[]>([]);
  const [workspaceOverridesAvailable, setWorkspaceOverridesAvailable] = useState(false);
  const [statusLoaded, setStatusLoaded] = useState(false);
  const [apiKeys, setApiKeys] = useState<Record<ProviderCredentialName, string>>({
    brave: "", exa: "", gemini: "", "google-maps": "", crawl4ai: "",
  });
  const [crawlEndpoint, setCrawlEndpoint] = useState("");
  const [crawlToken, setCrawlToken] = useState("");
  const [busyProvider, setBusyProvider] = useState<ProviderCredentialName | null>(null);
  const [feedback, setFeedback] = useState<Record<string, { message: string; succeeded: boolean }>>({});
  const [loadError, setLoadError] = useState<string | null>(null);

  const refresh = async () => {
    try {
      const result = await getProviderCredentials();
      setStatuses(result.credentials);
      setWorkspaceOverridesAvailable(result.workspaceOverridesAvailable);
      setLoadError(null);
    } catch {
      setLoadError("Provider credential status could not be loaded.");
    } finally {
      setStatusLoaded(true);
    }
  };

  useEffect(() => { void refresh(); }, []);

  const submit = async (provider: ProviderCredentialName) => {
    setBusyProvider(provider);
    setFeedback(current => ({ ...current, [provider]: { message: "", succeeded: true } }));
    try {
      const result = provider === "crawl4ai"
        ? await replaceProviderCredential(provider, { endpoint: crawlEndpoint, token: crawlToken })
        : await replaceProviderCredential(provider, { apiKey: apiKeys[provider] });
      setFeedback(current => ({ ...current, [provider]: result }));
      setApiKeys(current => ({ ...current, [provider]: "" }));
      if (provider === "crawl4ai") { setCrawlEndpoint(""); setCrawlToken(""); }
      await refresh();
    } catch (error) {
      setFeedback(current => ({ ...current, [provider]: { message: error instanceof Error ? error.message : "Credential could not be saved.", succeeded: false } }));
    } finally {
      setBusyProvider(null);
    }
  };

  const remove = async (provider: ProviderCredentialName) => {
    setBusyProvider(provider);
    try {
      const result = await removeProviderCredential(provider);
      setFeedback(current => ({ ...current, [provider]: result }));
      await refresh();
    } catch {
      setFeedback(current => ({ ...current, [provider]: { message: "Workspace override could not be removed.", succeeded: false } }));
    } finally {
      setBusyProvider(null);
    }
  };

  const test = async (provider: ProviderCredentialName) => {
    setBusyProvider(provider);
    try {
      const result = await testProviderCredential(provider);
      setFeedback(current => ({ ...current, [provider]: result }));
    } catch {
      setFeedback(current => ({ ...current, [provider]: { message: "Connection test could not be completed.", succeeded: false } }));
    } finally {
      setBusyProvider(null);
    }
  };

  return (
    <Panel title="Provider credentials" eyebrow="ADMIN · SECURE CONFIGURATION" className={styles.panel}>
      <div className={styles.intro}>
        <p>Workspace overrides are encrypted before they are stored. Removing an override restores the deployment environment or Secret Manager value, if configured.</p>
        {statusLoaded && !workspaceOverridesAvailable ? <p className={styles.warning} role="status">Secure workspace storage is disabled. Configure a valid RAVEN_CREDENTIAL_MASTER_KEY before saving overrides; deployment credentials remain usable.</p> : null}
        {loadError ? <p className={styles.error} role="alert">{loadError}</p> : null}
      </div>
      <div className={styles.providers}>
        {providers.map(provider => {
          const status = statuses.find(item => item.provider === provider.id);
          const feedbackItem = feedback[provider.id];
          const busy = busyProvider === provider.id;
          return <section className={styles.provider} key={provider.id} aria-labelledby={`credential-${provider.id}`}>
            <header className={styles.providerHeader}>
              <div><h3 id={`credential-${provider.id}`}>{provider.name}</h3><p>{provider.description}</p></div>
              <span className={status?.configured ? styles.configured : styles.missing}>{sourceLabel(status)}</span>
            </header>
            <div className={styles.form}>
              {provider.id === "crawl4ai" ? <>
                <label>Endpoint<input type="url" inputMode="url" autoComplete="url" placeholder="https://crawl.example.run.app" value={crawlEndpoint} onChange={event => setCrawlEndpoint(event.target.value)} /></label>
                <label>New Crawl4AI token<input type="password" autoComplete="new-password" spellCheck={false} value={crawlToken} onChange={event => setCrawlToken(event.target.value)} /></label>
              </> : <label>{provider.id === "google-maps" ? "New browser key" : "New API key"}<input type="password" autoComplete="new-password" spellCheck={false} value={apiKeys[provider.id]} onChange={event => setApiKeys(current => ({ ...current, [provider.id]: event.target.value }))} /></label>}
              <div className={styles.actions}>
                <button type="button" onClick={() => void submit(provider.id)} disabled={busy || !workspaceOverridesAvailable || (provider.id === "crawl4ai" ? !crawlEndpoint || !crawlToken : !apiKeys[provider.id])}>Replace</button>
                <button type="button" onClick={() => void test(provider.id)} disabled={busy || !status?.configured}>Test connection</button>
                {status?.source === "workspace" ? <button type="button" className={styles.quiet} onClick={() => void remove(provider.id)} disabled={busy}>Remove override</button> : null}
              </div>
            </div>
            {feedbackItem?.message ? <p className={feedbackItem.succeeded ? styles.success : styles.error} role={feedbackItem.succeeded ? "status" : "alert"}>{feedbackItem.message}</p> : null}
          </section>;
        })}
      </div>
    </Panel>
  );
}
