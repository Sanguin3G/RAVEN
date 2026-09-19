const acknowledgedResearchKey = "raven-acknowledged-research-review";

function readAcknowledgedIds() {
  if (typeof window === "undefined") return new Set<string>();
  try {
    const value: unknown = JSON.parse(window.localStorage.getItem(acknowledgedResearchKey) || "[]");
    return new Set(Array.isArray(value) ? value.filter((item): item is string => typeof item === "string") : []);
  } catch {
    return new Set<string>();
  }
}

export function getLegacyAcknowledgedResearchReviewIds() {
  return [...readAcknowledgedIds()];
}

export function clearLegacyAcknowledgedResearchReviewIds() {
  if (typeof window === "undefined") return;
  try { window.localStorage.removeItem(acknowledgedResearchKey); } catch { /* best effort */ }
}
