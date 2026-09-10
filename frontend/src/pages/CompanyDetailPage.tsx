import { useEffect, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { Panel } from "../components/Panel";
import { getApiErrorMessage } from "../api/client";
import { getCompany } from "../api/companies";
import type { Company } from "../types/company";

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "long", year: "numeric" }).format(new Date(value));
}

export function CompanyDetailPage() {
  const { id } = useParams();
  const [searchParams] = useSearchParams();
  const [company, setCompany] = useState<Company | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!id) { setLoading(false); return; }
    let active = true;
    getCompany(id)
      .then((result) => { if (active) setCompany(result); })
      .catch((reason: unknown) => { if (active) setError(getApiErrorMessage(reason, "Could not load this company.")); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [id]);

  if (loading) return <Panel className="narrow-page empty-state" title="Loading company" eyebrow="RAVEN API"><p>Fetching this company from the backend.</p></Panel>;

  if (!company) {
    return <Panel className="narrow-page empty-state" title="Company not found" eyebrow="MISSING RECORD"><p>{error || "RAVEN could not find that company record."}</p><Link className="button button--secondary" to="/companies">Return to Company List</Link></Panel>;
  }

  const generated = searchParams.get("generated") === "true";
  const manual = searchParams.get("manual") === "true";
  return (
    <div className="page-stack company-detail-page">
      <Link className="back-link" to="/companies">← Back to Company List</Link>
      {generated ? <div className="success-banner" role="status"><strong>Profile generation started.</strong> RAVEN is using the matched public sources to build this profile.</div> : null}
      {manual ? <div className="info-banner" role="status"><strong>Manual profile mode.</strong> Review the company details below and complete the fields with your own evidence.</div> : null}
      <article className="company-detail-card">
        <header className="company-detail-header"><div className="company-detail-heading"><span className="company-avatar company-avatar--large" aria-hidden="true">{company.name.slice(0, 2).toUpperCase()}</span><div><p className="eyebrow">COMPANY IDENTITY</p><h1>{company.name}</h1><p className="company-detail-subtitle">{company.country || "Country not provided"}</p></div></div></header>
        <div className="company-detail-body"><Panel title="Company overview" eyebrow="STABLE IDENTITY"><div className="detail-grid"><div><span>Country</span><strong>{company.country || "Not provided"}</strong></div><div><span>Website</span><strong>{company.website || "Not provided"}</strong></div><div><span>Created</span><strong>{formatDate(company.createdAt)}</strong></div><div><span>Last updated</span><strong>{formatDate(company.updatedAt)}</strong></div></div></Panel><Panel title="Identifiers &amp; sources" eyebrow="VERIFICATION"><dl className="definition-list"><div><dt>Website</dt><dd>{company.website ? <a href={company.website} target="_blank" rel="noreferrer">{company.website} ↗</a> : "Not provided"}</dd></div><div><dt>Company ID</dt><dd>{company.id}</dd></div><div><dt>Last updated</dt><dd>{formatDate(company.updatedAt)}</dd></div></dl></Panel></div>
      </article>
    </div>
  );
}
