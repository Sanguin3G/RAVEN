import type { ResearchRun } from "../types/research";

const currentResearchKey = "raven-current-research";

export type CurrentResearchSession = {
  runId: string;
  companyId: string;
  companyName: string;
  paused: boolean;
};

function storage() {
  if (typeof window === "undefined") return null;

  try {
    return window.sessionStorage;
  } catch {
    return null;
  }
}

export function readCurrentResearch(): CurrentResearchSession | null {
  const store = storage();
  if (!store) return null;

  try {
    const value: unknown = JSON.parse(store.getItem(currentResearchKey) || "null");
    if (!value || typeof value !== "object" || Array.isArray(value)) return null;
    const candidate = value as Partial<CurrentResearchSession>;
    if (typeof candidate.runId !== "string" || typeof candidate.companyId !== "string" || typeof candidate.companyName !== "string") return null;
    return { runId: candidate.runId, companyId: candidate.companyId, companyName: candidate.companyName, paused: candidate.paused === true };
  } catch {
    return null;
  }
}

export function rememberCurrentResearch(run: Pick<ResearchRun, "id" | "companyId">, companyName: string, paused = false) {
  const store = storage();
  if (!store) return;

  try {
    const value: CurrentResearchSession = {
      runId: run.id,
      companyId: run.companyId,
      companyName,
      paused,
    };
    store.setItem(currentResearchKey, JSON.stringify(value));
  } catch {
    // A blocked session storage context should not stop research.
  }
}

export function setCurrentResearchPaused(runId: string, paused: boolean) {
  const current = readCurrentResearch();
  if (!current || current.runId !== runId) return current;

  const next = { ...current, paused };
  const store = storage();
  try {
    store?.setItem(currentResearchKey, JSON.stringify(next));
  } catch {
    // A blocked session storage context should not stop research.
  }
  return next;
}

export function clearCurrentResearch(runId?: string) {
  const current = readCurrentResearch();
  if (runId && current?.runId !== runId) return;

  try {
    storage()?.removeItem(currentResearchKey);
  } catch {
    // A blocked session storage context should not stop navigation.
  }
}
