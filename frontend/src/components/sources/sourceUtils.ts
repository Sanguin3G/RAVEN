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
  tone: "official" | "topcv" | "linkedin" | "registry" | "github" | "news" | "external" | "search" | "generic";
};

const sourceLabels: Record<string, string> = {
  OfficialWebsite: "Official website",
  OfficialDocument: "Official document",
  BusinessRegistry: "Business registry",
  TopCv: "TopCV",
  LinkedIn: "LinkedIn",
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

export function sourceIconIdentity(kind?: SourceKind | null, domain?: string | null): SourceIconIdentity {
  const kindValue = normalizedKind(kind);
  const domainValue = (domain ?? "").toLowerCase();

  if (kindValue.includes("topcv") || domainValue.endsWith("topcv.vn")) {
    return { label: "TopCV", monogram: "TC", tone: "topcv" };
  }

  if (kindValue.includes("linkedin") || domainValue.endsWith("linkedin.com")) {
    return { label: "LinkedIn", monogram: "in", tone: "linkedin" };
  }

  if (kindValue.includes("github") || domainValue.endsWith("github.com")) {
    return { label: "GitHub", monogram: "GH", tone: "github" };
  }

  if (kindValue.includes("businessregistry") || kindValue.includes("registry") || kindValue.includes("gleif") || kindValue.includes("opencorporates")) {
    return { label: "Business registry", monogram: "BR", tone: "registry" };
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

  const host = (domain ?? "").replace(/^www\./i, "").trim();
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

export function formatRetrievedAt(value?: string | null): string | undefined {
  if (!value) return undefined;
  const timestamp = Date.parse(value);
  if (Number.isNaN(timestamp)) return undefined;
  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(timestamp);
}
