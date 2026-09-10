export type SourceKind =
  | "OfficialWebsite"
  | "OfficialDocument"
  | "BusinessRegistry"
  | "TopCv"
  | "LinkedIn"
  | "News"
  | "ExternalWebsite"
  | "SearchResult"
  | (string & {});

export type SourceIconIdentity = {
  label: string;
  monogram: string;
  tone: "official" | "topcv" | "linkedin" | "registry" | "github" | "news" | "brave" | "crawl4ai" | "gemini" | "external" | "search" | "generic";
  /** The canonical host used by the favicon service for provider identities. */
  faviconHost?: string;
};

const sourceLabels: Record<string, string> = {
  OfficialWebsite: "Official website",
  OfficialDocument: "Official document",
  BusinessRegistry: "Business registry",
  TopCv: "TopCV",
  LinkedIn: "LinkedIn",
  GitHub: "GitHub",
  Brave: "Brave Search",
  Crawl4AI: "Crawl4AI",
  Gemini: "Gemini",
  News: "News",
  ExternalWebsite: "External website",
  SearchResult: "Search result",
};

export function sourceKindLabel(kind?: SourceKind | null): string {
  if (!kind) return "Public source";
  return sourceLabels[kind] ?? kind.replace(/([a-z])([A-Z])/g, "$1 $2");
}

/** Only network URLs are accepted for user/provider-supplied links. */
export function safeExternalUrl(value?: string | null): string | undefined {
  if (!value) return undefined;

  try {
    const url = new URL(value);
    if (url.protocol !== "http:" && url.protocol !== "https:") return undefined;
    return url.href;
  } catch {
    return undefined;
  }
}

export function domainFromUrl(value?: string | null): string | undefined {
  const safeUrl = safeExternalUrl(value);
  if (!safeUrl) return undefined;

  try {
    return new URL(safeUrl).hostname.replace(/^www\./i, "").toLowerCase();
  } catch {
    return undefined;
  }
}

function normalizedKind(kind?: SourceKind | null): string {
  return (kind ?? "").replace(/[\s_-]/g, "").toLowerCase();
}

function normalizedDomain(value?: string | null): string | undefined {
  const candidate = value?.trim();
  if (!candidate) return undefined;

  try {
    const parsed = new URL(candidate.includes("://") ? candidate : `https://${candidate}`);
    if (parsed.protocol !== "http:" && parsed.protocol !== "https:") return undefined;
    if (parsed.username || parsed.password) return undefined;
    return parsed.hostname.replace(/^www\./i, "").replace(/\.$/, "").toLowerCase() || undefined;
  } catch {
    return undefined;
  }
}

function isHostOrSubdomain(host: string, root: string): boolean {
  return host === root || host.endsWith(`.${root}`);
}

export function sourceIconIdentity(kind?: SourceKind | null, domain?: string | null, provider?: string | null): SourceIconIdentity {
  const kindValue = normalizedKind(kind);
  const providerValue = normalizedKind(provider);
  const domainValue = normalizedDomain(domain) ?? "";

  if (kindValue.includes("topcv") || domainValue === "topcv.vn" || isHostOrSubdomain(domainValue, "topcv.vn")) {
    return { label: "TopCV", monogram: "TC", tone: "topcv", faviconHost: "topcv.vn" };
  }

  if (kindValue.includes("linkedin") || isHostOrSubdomain(domainValue, "linkedin.com")) {
    return { label: "LinkedIn", monogram: "in", tone: "linkedin", faviconHost: "linkedin.com" };
  }

  if (kindValue.includes("github") || isHostOrSubdomain(domainValue, "github.com")) {
    return { label: "GitHub", monogram: "GH", tone: "github", faviconHost: "github.com" };
  }

  if (kindValue.includes("businessregistry") || kindValue.includes("registry") || kindValue.includes("gleif") || kindValue.includes("opencorporates")) {
    return { label: "Business registry", monogram: "BR", tone: "registry", faviconHost: domainValue || "dangkykinhdoanh.gov.vn" };
  }

  if (kindValue.includes("brave") || providerValue.includes("brave")) {
    return { label: "Brave Search", monogram: "B", tone: "brave", faviconHost: "brave.com" };
  }

  if (kindValue.includes("crawl4ai") || providerValue.includes("crawl4ai")) {
    return { label: "Crawl4AI", monogram: "C4", tone: "crawl4ai", faviconHost: "crawl4ai.com" };
  }

  if (kindValue.includes("gemini") || providerValue.includes("gemini")) {
    return { label: "Gemini", monogram: "G", tone: "gemini", faviconHost: "ai.google.dev" };
  }

  if (kindValue.includes("officialwebsite") || kindValue.includes("officialdocument")) {
    return { label: kindValue.includes("document") ? "Official document" : "Official website", monogram: "R", tone: "official" };
  }

  if (kindValue.includes("news")) {
    return { label: "News", monogram: "N", tone: "news" };
  }

  if (kindValue.includes("searchresult")) {
    return { label: "Search result", monogram: "S", tone: "search" };
  }

  if (kindValue.includes("externalwebsite")) {
    return { label: "External website", monogram: "W", tone: "external" };
  }

  const host = domainValue;
  const monogram = host
    ? host
        .split(/[.\s-]+/)
        .filter(Boolean)
        .slice(0, 2)
        .map((part) => part[0])
        .join("")
        .toUpperCase()
        .slice(0, 2)
    : "?";

  return { label: host || "Public source", monogram, tone: "generic" };
}

export function displayDomain(domain?: string | null, url?: string | null): string | undefined {
  return domain?.trim() || domainFromUrl(url);
}

/**
 * Returns a safe, real favicon URL for a source/provider. The Google favicon
 * endpoint is deliberately only fed a parsed hostname, so arbitrary source
 * values cannot become an image URL or inject a protocol/query string.
 */
export function sourceFaviconUrl(kind?: SourceKind | null, domain?: string | null, pageUrl?: string | null, provider?: string | null): string | undefined {
  const sourceHost = normalizedDomain(domain) ?? domainFromUrl(pageUrl);
  const identity = sourceIconIdentity(kind, sourceHost, provider);
  const faviconHost = identity.faviconHost || sourceHost;
  if (!faviconHost) return undefined;

  return safeExternalUrl(`https://www.google.com/s2/favicons?domain=${encodeURIComponent(faviconHost)}&sz=64`);
}

export function formatRetrievedAt(value?: string | null): string | undefined {
  if (!value) return undefined;
  const timestamp = Date.parse(value);
  if (Number.isNaN(timestamp)) return undefined;
  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(timestamp);
}
