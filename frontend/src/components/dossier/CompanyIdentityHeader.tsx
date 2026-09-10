import { safeExternalUrl } from "../sources/sourceUtils";
import styles from "./dossier.module.css";
import type { DossierCompany, DossierProfile } from "./dossierTypes";

export interface CompanyIdentityHeaderProps {
  company: DossierCompany;
  profile?: DossierProfile | null;
}

function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return "?";
  return words.slice(0, 2).map((word) => word[0]).join("").toUpperCase();
}

function formatDate(value?: string | null): string | undefined {
  if (!value) return undefined;
  const timestamp = Date.parse(value);
  if (Number.isNaN(timestamp)) return undefined;
  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium" }).format(timestamp);
}

function valueOrUnknown(value?: string | null): string {
  return value?.trim() || "Not verified";
}

export function CompanyIdentityHeader({ company, profile }: CompanyIdentityHeaderProps) {
  const website = safeExternalUrl(profile?.website || company.website);
  const legalName = profile?.legalName || company.legalName;
  const industry = profile?.primaryIndustry || company.industry;
  const location = profile?.headquarters || company.headquarters || profile?.country || company.country;
  const lastResearched = formatDate(company.lastResearchedAt || profile?.confirmedAt);

  return (
    <header className={styles.identityHeader} data-testid="dossier-identity-header">
      <div className={styles.identityMain}>
        <span aria-hidden="true" className={styles.identityMark}>{initials(company.displayName)}</span>
        <div className={styles.identityCopy}>
          <p className={styles.eyebrow}>Company dossier</p>
          <h1>{company.displayName}</h1>
          <p className={styles.legalName}>{legalName ? valueOrUnknown(legalName) : "Legal identity not verified"}</p>
          <p className={styles.identityMeta}>
            {industry && <span>{industry}</span>}
            {location && <span>{location}</span>}
            {website ? <a href={website} rel="noreferrer noopener" target="_blank">Official website</a> : <span>Website not verified</span>}
          </p>
        </div>
      </div>
      <div className={styles.identityAside}>
        <strong>{lastResearched ? `Last researched ${lastResearched}` : "Not researched yet"}</strong>
        <span>{profile?.version ? `Profile version ${profile.version}` : "No accepted profile version"}</span>
      </div>
    </header>
  );
}
