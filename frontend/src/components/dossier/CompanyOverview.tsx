import { safeExternalUrl } from "../sources/sourceUtils";
import styles from "./dossier.module.css";
import type { DossierCompany, DossierLocation, DossierProfile } from "./dossierTypes";

export interface CompanyOverviewProps {
  company: DossierCompany;
  profile?: DossierProfile | null;
}

function UnknownValue() {
  return <span className={styles.unknown}>Not verified</span>;
}

function FieldValue({ value }: { value?: number | string | null }) {
  if (value === null || value === undefined || value === "") return <UnknownValue />;
  return <>{value}</>;
}

function locationLabel(location: DossierLocation): string {
  return [location.name, location.address, location.country].filter(Boolean).join(" — ") || "Location not verified";
}

export function CompanyOverview({ company, profile }: CompanyOverviewProps) {
  const current = profile || {};
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
          {summary ? <p className={styles.summary}>{summary}</p> : <p className={styles.summary}><UnknownValue /> — generate a profile from acquired evidence to add a grounded description.</p>}
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
        </section>

        <section className={styles.section} aria-labelledby="dossier-products-heading">
          <div className={styles.sectionHeader}><h2 id="dossier-products-heading">Products &amp; services</h2><span>{products.length ? `${products.length} listed` : "No verified entries"}</span></div>
          {products.length ? <ul className={styles.plainList}>{products.map((product, index) => <li key={`${product.name}-${index}`}><strong>{product.name}</strong>{(product.type || product.description) && <span>{[product.type, product.description].filter(Boolean).join(" · ")}</span>}</li>)}</ul> : <p className={styles.contextNote}>No supported products or services were found in the accepted evidence.</p>}
        </section>

        <section className={styles.section} aria-labelledby="dossier-markets-heading">
          <div className={styles.sectionHeader}><h2 id="dossier-markets-heading">Markets &amp; customer segments</h2><span>{markets.length ? `${markets.length} listed` : "No verified entries"}</span></div>
          {markets.length ? <ul className={styles.plainList}>{markets.map((market, index) => <li key={`${market.name}-${index}`}><strong>{market.name}</strong>{market.type && <span>{market.type}</span>}</li>)}</ul> : <p className={styles.contextNote}>No supported markets were found in the accepted evidence.</p>}
        </section>
      </div>

      <aside className={styles.contextStack} aria-label="Dossier context">
        <section className={styles.contextCard} aria-labelledby="dossier-identifiers-heading">
          <h2 id="dossier-identifiers-heading">Identifiers</h2>
          <dl className={styles.identifierList}>
            <div><dt>Legal name</dt><dd><FieldValue value={current.legalName || company.legalName} /></dd></div>
            <div><dt>Registration / tax ID</dt><dd><FieldValue value={current.registrationNumberOrTaxId || company.registrationNumber} /></dd></div>
            <div><dt>Official website</dt><dd>{website ? <a href={website} rel="noreferrer noopener" target="_blank">{new URL(website).hostname.replace(/^www\./i, "")}</a> : <UnknownValue />}</dd></div>
          </dl>
        </section>

        <section className={styles.contextCard} aria-labelledby="dossier-leadership-heading">
          <h2 id="dossier-leadership-heading">Leadership</h2>
          {leadership.length ? <ul className={styles.plainList}>{leadership.map((leader, index) => <li key={`${leader.name}-${index}`}><strong>{leader.name}</strong>{leader.title && <span>{leader.title}</span>}</li>)}</ul> : <p className={styles.contextNote}>No explicitly associated leaders were verified.</p>}
        </section>

        <section className={styles.contextCard} aria-labelledby="dossier-locations-heading">
          <h2 id="dossier-locations-heading">Locations</h2>
          {locations.length ? <ul className={styles.plainList}>{locations.map((location, index) => <li key={`${locationLabel(location)}-${index}`}><strong>{locationLabel(location)}</strong>{location.type && <span>{location.type}</span>}</li>)}</ul> : <p className={styles.contextNote}>No operating locations were verified.</p>}
        </section>

        <section className={styles.contextCard} aria-labelledby="dossier-links-heading">
          <h2 id="dossier-links-heading">Public links</h2>
          {links.length || website ? <ul className={styles.linkList}>{website && <li><strong>Official website</strong><a href={website} rel="noreferrer noopener" target="_blank">{new URL(website).hostname.replace(/^www\./i, "")}</a></li>}{links.map((link, index) => { const safeLink = safeExternalUrl(link); return safeLink ? <li key={`${safeLink}-${index}`}><strong>Public source</strong><a href={safeLink} rel="noreferrer noopener" target="_blank">{new URL(safeLink).hostname.replace(/^www\./i, "")}</a></li> : null; })}</ul> : <p className={styles.contextNote}>No public links were verified.</p>}
        </section>
      </aside>
    </div>
  );
}
