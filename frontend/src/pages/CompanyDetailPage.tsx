import { Link, useParams, useSearchParams } from "react-router-dom";
import { Panel } from "../components/Panel";
import { getMockCompany } from "../data/mockCompanies";

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "long", year: "numeric" }).format(new Date(value));
}

export function CompanyDetailPage() {
  const { id } = useParams();
  const [searchParams] = useSearchParams();
  const company = id ? getMockCompany(id) : null;

  if (!company) {
    return <Panel className="narrow-page empty-state" title="Company not found" eyebrow="MISSING RECORD"><p>RAVEN could not find that company record.</p><Link className="button button--secondary" to="/companies">Return to Company List</Link></Panel>;
  }

  const generated = searchParams.get("generated") === "true";
  const manual = searchParams.get("manual") === "true";
  return (
    <div className="page-stack company-detail-page">
      <Link className="back-link" to="/companies">← Back to Company List</Link>
      {generated ? <div className="success-banner" role="status"><strong>Profile generation started.</strong> RAVEN is using the matched public sources to build this profile.</div> : null}
      {manual ? <div className="info-banner" role="status"><strong>Manual profile mode.</strong> Review the company details below and complete the fields with your own evidence.</div> : null}
      <article className="company-detail-card">
        <header className="company-detail-header"><div className="company-detail-heading"><span className="company-avatar company-avatar--large" aria-hidden="true">{company.logo}</span><div><p className="eyebrow">COMPANY PROFILE</p><h1>{company.name}</h1><p className="company-detail-subtitle">{company.industry} · {company.country}</p></div></div><span className={`status status--large status--${company.status?.toLowerCase().replaceAll(" ", "-")}`}>{company.status}</span></header>
        <div className="company-detail-body"><Panel title="Company overview" eyebrow="PROFILE SUMMARY"><p className="company-summary">{company.summary}</p><div className="detail-grid"><div><span>Country</span><strong>{company.country}</strong></div><div><span>Industry</span><strong>{company.industry}</strong></div><div><span>Headquarters</span><strong>{company.headquarters}</strong></div><div><span>Company size</span><strong>{company.employees}</strong></div></div></Panel><Panel title="Identifiers &amp; sources" eyebrow="VERIFICATION"><dl className="definition-list"><div><dt>Website</dt><dd><a href={company.website ?? undefined} target="_blank" rel="noreferrer">{company.website}</a></dd></div><div><dt>LinkedIn</dt><dd><a href={company.linkedinUrl ?? undefined} target="_blank" rel="noreferrer">View company page ↗</a></dd></div><div><dt>Last updated</dt><dd>{formatDate(company.updatedAt)}</dd></div><div><dt>Profile status</dt><dd>{company.status}</dd></div></dl></Panel></div>
      </article>
    </div>
  );
}
