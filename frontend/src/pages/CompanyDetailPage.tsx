import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { getCompany } from "../api/companies";
import { ApiError, getApiErrorMessage } from "../api/client";
import { Panel } from "../components/Panel";
import { Tabs } from "../components/Tabs";
import { displayValue, displayWebsite, getWebsiteUrl } from "../features/companies/companyPresentation";
import type { Company } from "../types/company";

const profileTabs = [
  { id: "overview", label: "Overview" },
  { id: "sources", label: "Sources", suffix: "Later", disabled: true },
  { id: "changes", label: "Changes", suffix: "Later", disabled: true },
  { id: "ask", label: "Ask RAVEN", suffix: "Later", disabled: true },
];

export function CompanyDetailPage() {
  const { id } = useParams();
  const [company, setCompany] = useState<Company | null>(null);
  const [loading, setLoading] = useState(true);
  const [notFound, setNotFound] = useState(false);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!id) {
      setLoading(false);
      setNotFound(true);
      return;
    }

    let current = true;
    setLoading(true);
    setCompany(null);
    setError("");
    setNotFound(false);

    getCompany(id)
      .then((result) => { if (current) setCompany(result); })
      .catch((requestError: unknown) => {
        if (!current) return;
        if (requestError instanceof ApiError && requestError.status === 404) {
          setNotFound(true);
          return;
        }
        setError(getApiErrorMessage(requestError, "Could not load this company."));
      })
      .finally(() => { if (current) setLoading(false); });

    return () => { current = false; };
  }, [id]);

  if (loading) return <p className="state-message">Loading company profile…</p>;

  if (notFound) {
    return (
      <Panel className="narrow-page empty-state" title="Company not found" eyebrow="MISSING RECORD">
        <p>RAVEN could not find that company record.</p>
        <Link className="button button--secondary" to="/companies">Return to Companies</Link>
      </Panel>
    );
  }

  if (error) return <p className="state-message state-message--error" role="alert">{error}</p>;
  if (!company) return null;

  const websiteUrl = getWebsiteUrl(company.website);
  return (
    <article className="company-profile">
      <header className="company-profile__header">
        <p className="eyebrow">COMPANY PROFILE</p>
        <h1>{company.name}</h1>
        <p className="company-profile__meta">
          {displayValue(company.country)}
          {websiteUrl ? <><span aria-hidden="true"> · </span><a href={websiteUrl} target="_blank" rel="noreferrer">{displayWebsite(company.website)}</a></> : null}
        </p>
      </header>

      <Tabs items={profileTabs} activeId="overview" />

      <Panel className="profile-overview" title="About" eyebrow="OVERVIEW">
        <div className="research-empty-state">
          <h3>Research has not started yet.</h3>
          <p>RAVEN will eventually gather public sources and build a standardized Company Profile here.</p>
        </div>
        <div className="information-block">
          <h3>Company information</h3>
          <dl className="definition-list">
            <div><dt>Name</dt><dd>{company.name}</dd></div>
            <div><dt>Country</dt><dd>{displayValue(company.country)}</dd></div>
            <div><dt>Website</dt><dd>{websiteUrl ? <a href={websiteUrl} target="_blank" rel="noreferrer">{displayWebsite(company.website)}</a> : "—"}</dd></div>
          </dl>
        </div>
      </Panel>
    </article>
  );
}
