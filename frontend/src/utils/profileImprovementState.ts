const profileImprovementKey = "raven-profile-improvement-materials";

type StoredImprovements = Record<string, string[]>;

function readAll(): StoredImprovements {
  if (typeof window === "undefined") return {};
  try {
    const value: unknown = JSON.parse(window.localStorage.getItem(profileImprovementKey) || "{}");
    if (!value || typeof value !== "object" || Array.isArray(value)) return {};
    return Object.fromEntries(Object.entries(value).map(([companyId, ids]) => [
      companyId,
      Array.isArray(ids) ? ids.filter((id): id is string => typeof id === "string") : [],
    ]));
  } catch {
    return {};
  }
}

export function readProfileImprovementMaterialIds(companyId: string) {
  return new Set(readAll()[companyId] || []);
}

export function rememberProfileImprovementMaterial(companyId: string, materialId: string) {
  if (typeof window === "undefined" || !materialId) return;
  const all = readAll();
  all[companyId] = [...new Set([...(all[companyId] || []), materialId])].slice(-200);
  try {
    window.localStorage.setItem(profileImprovementKey, JSON.stringify(all));
  } catch {
    // A blocked storage context should not prevent the profile update.
  }
}
