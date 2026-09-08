export function displayValue(value?: string | null) {
  return value?.trim() || "—";
}

export function getWebsiteUrl(website?: string | null) {
  if (!website?.trim()) {
    return null;
  }

  return /^https?:\/\//i.test(website) ? website : `https://${website}`;
}

export function displayWebsite(website?: string | null) {
  return website?.replace(/^https?:\/\//i, "").replace(/\/$/, "") || "—";
}
