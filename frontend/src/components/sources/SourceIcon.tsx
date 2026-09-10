import { useEffect, useState } from "react";
import type { Icon } from "@phosphor-icons/react/dist/lib/types";
import { Buildings } from "@phosphor-icons/react/dist/csr/Buildings";
import { Briefcase } from "@phosphor-icons/react/dist/csr/Briefcase";
import { DownloadSimple } from "@phosphor-icons/react/dist/csr/DownloadSimple";
import { GithubLogo } from "@phosphor-icons/react/dist/csr/GithubLogo";
import { GlobeSimple } from "@phosphor-icons/react/dist/csr/GlobeSimple";
import { IdentificationCard } from "@phosphor-icons/react/dist/csr/IdentificationCard";
import { LinkedinLogo } from "@phosphor-icons/react/dist/csr/LinkedinLogo";
import { LinkSimple } from "@phosphor-icons/react/dist/csr/LinkSimple";
import { Lightning } from "@phosphor-icons/react/dist/csr/Lightning";
import { MagnifyingGlass } from "@phosphor-icons/react/dist/csr/MagnifyingGlass";
import { Newspaper } from "@phosphor-icons/react/dist/csr/Newspaper";
import { Sparkle } from "@phosphor-icons/react/dist/csr/Sparkle";
import styles from "./sources.module.css";
import { displayDomain, safeExternalUrl, sourceFaviconUrl, sourceIconIdentity, type SourceIconIdentity, type SourceKind } from "./sourceUtils";

export type SourceIconProps = {
  kind?: SourceKind | null;
  domain?: string | null;
  iconUrl?: string | null;
  /** Provider name is optional metadata for evidence cards (e.g. Crawl4AI). */
  provider?: string | null;
  label?: string;
  size?: "small" | "medium" | "large";
};

const fallbackIcons: Record<SourceIconIdentity["tone"], Icon> = {
  official: GlobeSimple,
  topcv: Briefcase,
  linkedin: LinkedinLogo,
  registry: IdentificationCard,
  github: GithubLogo,
  news: Newspaper,
  brave: Lightning,
  crawl4ai: DownloadSimple,
  gemini: Sparkle,
  external: LinkSimple,
  search: MagnifyingGlass,
  generic: Buildings,
};

const iconDimensions = {
  small: 20,
  medium: 28,
  large: 40,
} as const;

export function SourceIcon({ kind, domain, iconUrl, label, provider, size = "medium" }: SourceIconProps) {
  const safeIconUrl = safeExternalUrl(iconUrl);
  const displayHost = displayDomain(domain, iconUrl);
  const identity = sourceIconIdentity(kind, displayHost, provider);
  const derivedFaviconUrl = sourceFaviconUrl(kind, displayHost, undefined, provider);
  const imageSources = [safeIconUrl, derivedFaviconUrl].filter((url, index, urls): url is string => Boolean(url) && urls.indexOf(url) === index);
  const [failedSourceCount, setFailedSourceCount] = useState(0);

  useEffect(() => {
    setFailedSourceCount(0);
  }, [safeIconUrl, derivedFaviconUrl]);

  const accessibleName = label || identity.label;
  const imageUrl = imageSources[failedSourceCount];
  const FallbackIcon = fallbackIcons[identity.tone];

  return (
    <span
      aria-label={`${accessibleName} source icon`}
      className={`${styles.sourceIcon} ${styles[`sourceIcon${size[0].toUpperCase()}${size.slice(1)}`]} ${styles[`sourceIconTone${identity.tone[0].toUpperCase()}${identity.tone.slice(1)}`]}`}
      role="img"
    >
      {imageUrl ? (
        <img
          alt=""
          height={iconDimensions[size]}
          loading="lazy"
          onError={() => setFailedSourceCount((count) => count + 1)}
          src={imageUrl}
          width={iconDimensions[size]}
        />
      ) : (
        <FallbackIcon aria-hidden="true" className={styles.sourceIconGlyph} size={iconDimensions[size] - 2} weight="duotone" />
      )}
    </span>
  );
}
