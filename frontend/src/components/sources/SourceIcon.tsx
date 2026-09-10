import { useEffect, useState } from "react";
import styles from "./sources.module.css";
import { displayDomain, safeExternalUrl, sourceIconIdentity, type SourceKind } from "./sourceUtils";

export type SourceIconProps = {
  kind?: SourceKind | null;
  domain?: string | null;
  iconUrl?: string | null;
  label?: string;
  size?: "small" | "medium" | "large";
};

export function SourceIcon({ kind, domain, iconUrl, label, size = "medium" }: SourceIconProps) {
  const safeIconUrl = safeExternalUrl(iconUrl);
  const displayHost = displayDomain(domain, iconUrl);
  const identity = sourceIconIdentity(kind, displayHost);
  const [imageFailed, setImageFailed] = useState(false);

  useEffect(() => {
    setImageFailed(false);
  }, [safeIconUrl]);

  const accessibleName = label || identity.label;

  return (
    <span
      aria-label={`${accessibleName} source icon`}
      className={`${styles.sourceIcon} ${styles[`sourceIcon${size[0].toUpperCase()}${size.slice(1)}`]} ${styles[`sourceIconTone${identity.tone[0].toUpperCase()}${identity.tone.slice(1)}`]}`}
      role="img"
    >
      {safeIconUrl && !imageFailed ? (
        <img
          alt=""
          height={size === "small" ? 20 : size === "large" ? 40 : 28}
          loading="lazy"
          onError={() => setImageFailed(true)}
          src={safeIconUrl}
          width={size === "small" ? 20 : size === "large" ? 40 : 28}
        />
      ) : (
        <span aria-hidden="true" className={styles.sourceIconMonogram}>
          {identity.monogram}
        </span>
      )}
    </span>
  );
}
