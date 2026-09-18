import { useSyncExternalStore } from "react";

export type ResearchActivityOrigin = "Native" | "Deep" | "External";
export type ResearchActivityStatus = "running" | "ready" | "failed";

export interface ResearchActivity {
  id: string;
  jobId?: string;
  origin: ResearchActivityOrigin;
  companyId: string;
  companyName: string;
  objective: string;
  detail: string;
  status: ResearchActivityStatus;
  href?: string;
  onOpen?: () => void;
  updatedAt: string;
  /** Completed research that must wait for an accepted profile before review. */
  locked?: boolean;
}

const dismissedActivityKey = "raven-dismissed-research-activities";

function readDismissedActivityIds() {
  if (typeof window === "undefined") return new Set<string>();
  try {
    const value: unknown = JSON.parse(window.localStorage.getItem(dismissedActivityKey) || "[]");
    return new Set(Array.isArray(value) ? value.filter((item): item is string => typeof item === "string") : []);
  } catch {
    return new Set<string>();
  }
}

let activities: ResearchActivity[] = [];
const dismissedActivityIds = readDismissedActivityIds();
const listeners = new Set<() => void>();

function notify() {
  listeners.forEach((listener) => listener());
}

export function upsertResearchActivity(activity: ResearchActivity) {
  if (activity.status === "ready" && !activity.locked && dismissedActivityIds.has(activity.id)) return;
  if (activity.status === "running") dismissedActivityIds.delete(activity.id);
  activities = [activity, ...activities.filter((item) => item.id !== activity.id)].slice(0, 20);
  notify();
}

export function updateResearchActivity(id: string, update: Partial<ResearchActivity>) {
  const existing = activities.find((activity) => activity.id === id);
  if (!existing) return;
  upsertResearchActivity({ ...existing, ...update, updatedAt: new Date().toISOString() });
}

export function hasResearchActivity(id: string) {
  return activities.some((activity) => activity.id === id);
}

export function dismissResearchActivity(id: string) {
  dismissedActivityIds.add(id);
  activities = activities.filter((activity) => activity.id !== id);
  try {
    window.localStorage.setItem(dismissedActivityKey, JSON.stringify([...dismissedActivityIds].slice(-200)));
  } catch {
    // A blocked storage context should not stop the user opening the result.
  }
  notify();
}

export function useResearchActivities() {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    () => activities,
    () => [],
  );
}
