import { useEffect, useMemo, useRef, useState } from "react";
import { safeExternalUrl } from "../sources/sourceUtils";
import type { CoverageLevel, ResearchTarget } from "../../api/coverage";
import type { CompanyProfileVersion } from "../../types/profile";
import styles from "./dossier.module.css";
import { CompanyCoveragePanel } from "./CompanyCoveragePanel";
import { TargetedEnrichmentPanel } from "./TargetedEnrichmentPanel";
import type { DossierCompany, DossierCoverage, DossierLocation, DossierProfile } from "./dossierTypes";

export interface CompanyOverviewProps {
  company: DossierCompany;
  profile?: DossierProfile | null;
  coverage?: DossierCoverage | null;
  initialEnrichmentTargets?: ResearchTarget[];
  openEnrichment?: boolean;
  onProfileConfirmed?: (profile: CompanyProfileVersion) => void;
}

function UnknownValue() {
  return <span className={styles.unknown}>Not verified</span>;
}

function FieldValue({ value }: { value?: number | string | null }) {
  return value === null || value === undefined || value === "" ? <UnknownValue /> : <>{value}</>;
}

function locationLabel(location: DossierLocation): string {
  return [location.name, location.address, location.country].filter(Boolean).join(" - ") || "Location not verified";
}

const targetLabels: Record<ResearchTarget, string> = {
  LegalIdentity: "legal identity", TaxRegistration: "tax registration", FoundedHistory: "founded history",
  Industry: "industry", EmployeeScale: "company scale", ProductsServices: "products and services",
  Markets: "markets", Leadership: "leadership", Locations: "locations",
};

function coverageLevel(response: DossierCoverage["response"] | undefined, target: ResearchTarget): CoverageLevel {
  return response?.items.find((item) => item.target === target)?.level ?? "Missing";
}

function ResearchAction({ target, level, onClick }: { target: ResearchTarget; level: CoverageLevel; onClick: () => void }) {
  if (level !== "Missing" && level !== "Weak") return null;
  return <button className={styles.coverageAction} type="button" onClick={onClick}>{level === "Missing" ? `Find ${targetLabels[target]}` : `Strengthen ${targetLabels[target]}`}</button>;
}

export function CompanyOverview({ company, profile, coverage, initialEnrichmentTargets, openEnrichment = false, onProfileConfirmed }: CompanyOverviewProps) {
  const [displayProfile, setDisplayProfile] = useState<DossierProfile | null | undefined>(profile);
  const [enrichmentTargets, setEnrichmentTargets] = useState<ResearchTarget[]>([]);
  const [enrichmentOpen, setEnrichmentOpen] = useState(false);
  const autoOpenRef = useRef(false);
  const current = displayProfile || {};

  useEffect(() => setDisplayProfile(profile), [profile]);

  useEffect(() => {
    if (!openEnrichment) {
      autoOpenRef.current = false;
      return;
    }
    if (autoOpenRef.current) return;
    autoOpenRef.current = true;
    setEnrichmentTargets(initialEnrichmentTargets?.length ? initialEnrichmentTargets : gapTargetsFallback(profile, coverage));
    setEnrichmentOpen(true);
  }, [openEnrichment, initialEnrichmentTargets, profile, coverage]);

  const gapTargets = useMemo(() => {
    const all: ResearchTarget[] = ["LegalIdentity", "TaxRegistration", "FoundedHistory", "Industry", "EmployeeScale", "ProductsServices", "Markets", "Leadership", "Locations"];
    return all.filter((target) => {
      const level = coverageLevel(coverage?.response, target);
      return level === "Missing" || level === "Weak";
    });
  }, [coverage]);

  function beginEnrichment(targets: ResearchTarget[]) {
    setEnrichmentTargets(targets.length ? targets : ["LegalIdentity", "Markets", "Leadership"]);
    setEnrichmentOpen(true);
  }

  function handleConfirmed(nextProfile: CompanyProfileVersion) {
    setDisplayProfile({
      ...nextProfile,
      publicLinks: nextProfile.publicLinks?.map((link) => link.url) ?? [],
    });
    onProfileConfirmed?.(nextProfile);
  }

  const summary = current.summary?.trim();
  const products = current.productsServices ?? [];
  const markets = current.markets ?? [];
  const leadership = current.leadership ?? [];
  const locations = current.locations ?? [];
  const links = current.publicLinks ?? [];
  const website = safeExternalUrl(current.website || company.website);

  return (
    <div className={styles.contentGrid} data-testid="dossier-overview">
      <div className={styles.sectionStack}>
        <section className={styles.section} aria-labelledby="dossier-about-heading">
          <div className={styles.sectionHeader}><h2 id="dossier-about-heading">About</h2><span>Accepted facts only</span></div>
          {summary ? <p className={styles.summary}>{summary}</p> : <p className={styles.summary}><UnknownValue /> - generate a profile from acquired evidence to add a grounded description.</p>}
        </section>

        <section className={styles.section} aria-labelledby="dossier-glance-heading">
          <div className={styles.sectionHeader}><h2 id="dossier-glance-heading">At a glance</h2></div>
          <dl className={styles.atAGlance}>
            <div><dt>Founded</dt><dd><FieldValue value={current.foundedYear} /></dd></div>
            <div><dt>Employees / scale</dt><dd><FieldValue value={current.employeeCountRange || current.companySize || current.employeeCount} /></dd></div>
            <div><dt>Headquarters</dt><dd><FieldValue value={current.headquarters || company.headquarters} /></dd></div>
            <div><dt>Industry</dt><dd><FieldValue value={current.primaryIndustry || company.industry} /></dd></div>
            <div><dt>Country</dt><dd><FieldValue value={current.country || company.country} /></dd></div>
          </dl>
          <div className={styles.coverageActions}>
            <ResearchAction target="FoundedHistory" level={coverageLevel(coverage?.response, "FoundedHistory")} onClick={() => beginEnrichment(["FoundedHistory"])} />
            <ResearchAction target="EmployeeScale" level={coverageLevel(coverage?.response, "EmployeeScale")} onClick={() => beginEnrichment(["EmployeeScale"])} />
            <ResearchAction target="Industry" level={coverageLevel(coverage?.response, "Industry")} onClick={() => beginEnrichment(["Industry"])} />
          </div>
        </section>

        <section className={styles.section} aria-labelledby="dossier-products-heading">
          <div className={styles.sectionHeader}><h2 id="dossier-products-heading">Products and services</h2><span>{products.length ? `${products.length} listed` : "No verified entries"}</span></div>
          {products.length > 0 ? (
            <ul className={styles.plainList}>
              {products.map((product, index) => (
                <li key={`${product.name}-${index}`}><strong>{product.name}</strong>{(product.type || product.description) && <span>{[product.type, product.description].filter(Boolean).join(" - ")}</span>}</li>
              ))}
            </ul>
          ) : <p className={styles.contextNote}>No supported products or services were found in the accepted evidence.</p>}
          {products.length === 0 && <div className={styles.coverageActions}><ResearchAction target="ProductsServices" level={coverageLevel(coverage?.response, "ProductsServices")} onClick={() => beginEnrichment(["ProductsServices"])} /></div>}
        </section>

        <section className={styles.section} aria-labelledby="dossier-markets-heading">
          <div className={styles.sectionHeader}><h2 id="dossier-markets-heading">Markets and customer segments</h2><span>{markets.length ? `${markets.length} listed` : "No verified entries"}</span></div>
          {markets.length > 0 ? (
            <ul className={styles.plainList}>
              {markets.map((market, index) => <li key={`${market.name}-${index}`}><strong>{market.name}</strong>{market.type && <span>{market.type}</span>}</li>)}
            </ul>
          ) : <p className={styles.contextNote}>No supported markets were found in the accepted evidence.</p>}
          {markets.length === 0 && <div className={styles.coverageActions}><ResearchAction target="Markets" level={coverageLevel(coverage?.response, "Markets")} onClick={() => beginEnrichment(["Markets"])} /></div>}
        </section>
      </div>

      <aside className={styles.contextStack} aria-label="Dossier context">
        <CompanyCoveragePanel coverage={coverage} />
        <section className={styles.contextCard} aria-labelledby="dossier-improve-heading">
          <div className={styles.sectionHeader}><h2 id="dossier-improve-heading">Improve this profile</h2><span>{gapTargets.length ? `${gapTargets.length} area${gapTargets.length === 1 ? "" : "s"} to strengthen` : "No obvious gaps"}</span></div>
          <p className={styles.contextNote}>Targeted research adds evidence to selected gaps and preserves unrelated accepted fields.</p>
          <div className={styles.coverageActions}><button className="button button--secondary" type="button" onClick={() => beginEnrichment(gapTargets)}>Improve this profile</button></div>
        </section>
        <section className={styles.contextCard} aria-labelledby="dossier-identifiers-heading">
          <h2 id="dossier-identifiers-heading">Identifiers</h2>
          <dl className={styles.identifierList}>
            <div><dt>Legal name</dt><dd><FieldValue value={current.legalName || company.legalName} /></dd></div>
            <div><dt>Registration / tax ID</dt><dd><FieldValue value={current.registrationNumberOrTaxId || company.registrationNumber} /></dd></div>
            <div><dt>Official website</dt><dd>{website ? <a href={website} rel="noreferrer noopener" target="_blank">{new URL(website).hostname.replace(/^www\./i, "")}</a> : <UnknownValue />}</dd></div>
          </dl>
          <div className={styles.coverageActions}><ResearchAction target="LegalIdentity" level={coverageLevel(coverage?.response, "LegalIdentity")} onClick={() => beginEnrichment(["LegalIdentity"])} /><ResearchAction target="TaxRegistration" level={coverageLevel(coverage?.response, "TaxRegistration")} onClick={() => beginEnrichment(["TaxRegistration"])} /></div>
        </section>

        <section className={styles.contextCard} aria-labelledby="dossier-leadership-heading">
          <h2 id="dossier-leadership-heading">Leadership</h2>
          {leadership.length > 0 ? <ul className={styles.plainList}>{leadership.map((leader, index) => <li key={`${leader.name}-${index}`}><strong>{leader.name}</strong>{leader.title && <span>{leader.title}</span>}</li>)}</ul> : <p className={styles.contextNote}>No explicitly associated leaders were verified.</p>}
          {leadership.length === 0 && <div className={styles.coverageActions}><ResearchAction target="Leadership" level={coverageLevel(coverage?.response, "Leadership")} onClick={() => beginEnrichment(["Leadership"])} /></div>}
        </section>

        <section className={styles.contextCard} aria-labelledby="dossier-locations-heading">
          <h2 id="dossier-locations-heading">Locations</h2>
          {locations.length > 0 ? <ul className={styles.plainList}>{locations.map((location, index) => <li key={`${locationLabel(location)}-${index}`}><strong>{locationLabel(location)}</strong>{location.type && <span>{location.type}</span>}</li>)}</ul> : <p className={styles.contextNote}>No operating locations were verified.</p>}
          {locations.length === 0 && <div className={styles.coverageActions}><ResearchAction target="Locations" level={coverageLevel(coverage?.response, "Locations")} onClick={() => beginEnrichment(["Locations"])} /></div>}
        </section>

        <section className={styles.contextCard} aria-labelledby="dossier-links-heading">
          <h2 id="dossier-links-heading">Public links</h2>
          {links.length || website ? <ul className={styles.linkList}>{website && <li><strong>Official website</strong><a href={website} rel="noreferrer noopener" target="_blank">{new URL(website).hostname.replace(/^www\./i, "")}</a></li>}{links.map((link, index) => { const safeLink = safeExternalUrl(link); return safeLink ? <li key={`${safeLink}-${index}`}><strong>Public source</strong><a href={safeLink} rel="noreferrer noopener" target="_blank">{new URL(safeLink).hostname.replace(/^www\./i, "")}</a></li> : null; })}</ul> : <p className={styles.contextNote}>No public links were verified.</p>}
        </section>
      </aside>
      <TargetedEnrichmentPanel company={company} profile={displayProfile} initialTargets={enrichmentTargets} open={enrichmentOpen} onClose={() => setEnrichmentOpen(false)} onConfirmed={handleConfirmed} />
    </div>
  );
}

function gapTargetsFallback(profile: DossierProfile | null | undefined, coverage: DossierCoverage | null | undefined): ResearchTarget[] {
  const all: ResearchTarget[] = ["LegalIdentity", "TaxRegistration", "FoundedHistory", "Industry", "EmployeeScale", "ProductsServices", "Markets", "Leadership", "Locations"];
  const current = profile || {};
  const hasValue: Partial<Record<ResearchTarget, boolean>> = {
    LegalIdentity: Boolean(current.legalName),
    TaxRegistration: Boolean(current.registrationNumberOrTaxId),
    FoundedHistory: Boolean(current.foundedYear),
    Industry: Boolean(current.primaryIndustry),
    EmployeeScale: Boolean(current.companySize || current.employeeCount || current.employeeCountRange),
    ProductsServices: Boolean(current.productsServices?.length),
    Markets: Boolean(current.markets?.length),
    Leadership: Boolean(current.leadership?.length),
    Locations: Boolean(current.locations?.length),
  };
  const gaps = all.filter((target) => {
    const level = coverage?.response?.items.find((item) => item.target === target)?.level;
    return level === "Missing" || level === "Weak" || (level === undefined && !hasValue[target]);
  });
  return gaps.length ? gaps : ["LegalIdentity", "Markets", "Leadership"];
}
