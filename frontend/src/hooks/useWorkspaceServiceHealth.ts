import { useEffect, useState } from "react";
import { getApiHealth, getProviderStatus } from "../api/system";

export type WorkspaceServiceState = "checking" | "operational" | "attention" | "unavailable" | "not-configured";

export function useWorkspaceServiceHealth() {
  const [state, setState] = useState<WorkspaceServiceState>("checking");

  useEffect(() => {
    let active = true;
    const checkServices = async () => {
      const [apiResult, providerResult] = await Promise.allSettled([getApiHealth(), getProviderStatus()]);
      if (!active) return;
      if (apiResult.status !== "fulfilled" || !apiResult.value.available || providerResult.status !== "fulfilled") {
        setState("attention");
        return;
      }
      const providers = [providerResult.value.brave, providerResult.value.exa, providerResult.value.crawl4Ai, providerResult.value.gemini].filter(Boolean);
      const configuredProviders = providers.filter((provider) => provider.configured);
      if (configuredProviders.length === 0) {
        setState("not-configured");
      } else if (configuredProviders.some((provider) => provider.available === false)) {
        setState("unavailable");
      } else {
        setState("operational");
      }
    };
    void checkServices();
    const intervalId = window.setInterval(() => { void checkServices(); }, 30_000);
    return () => { active = false; window.clearInterval(intervalId); };
  }, []);

  return state;
}
