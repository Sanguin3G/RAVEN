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

export function isResearchReviewAcknowledged(id: string) {
  return readAcknowledgedIds().has(id);
}

export function acknowledgeResearchReview(id: string) {
  if (typeof window === "undefined") return;
  const ids = readAcknowledgedIds();
  ids.add(id);
  try {
    window.localStorage.setItem(acknowledgedResearchKey, JSON.stringify([...ids].slice(-200)));
  } catch {
    // A blocked storage context should not prevent opening the research result.
  }
}
