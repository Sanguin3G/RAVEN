import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Panel } from "../components/Panel";
import { getApiErrorMessage } from "../api/client";
import { getCompanies } from "../api/companies";
import type { Company } from "../types/company";

function getInitials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]).join("").toUpperCase() || "?";
}

export function DashboardPage() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getCompanies()
      .then(setCompanies)
      .catch((reason: unknown) => setError(getApiErrorMessage(reason, "Could not load dashboard data.")))
      .finally(() => setLoading(false));
  }, []);

  const recentCompanies = [...companies].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)).slice(0, 4);

  return (
    <div className="page-stack dashboard-page">
      <section className="dashboard-hero" aria-labelledby="dashboard-title">
        <div>
          <p className="eyebrow">RAVEN WORKSPACE</p>
          <h1 id="dashboard-title">Company intelligence, at a glance.</h1>
          <p className="dashboard-hero__intro">Keep your company universe organised and turn trusted public sources into profiles your team can use.</p>
        </div>
        <div className="dashboard-hero__actions">
          <Link className="button" to="/companies/new">+ Add Company Profile</Link>
          <Link className="button button--secondary" to="/companies">View Company List</Link>
        </div>
      </section>

      <section className="stats-grid" aria-label="Workspace summary">
        <div className="stat-card"><span className="stat-card__label">Tracked companies</span><strong>{loading ? "—" : companies.length}</strong><span className="stat-card__detail">From the RAVEN API</span></div>
        <div className="stat-card"><span className="stat-card__label">Company identities</span><strong>{loading ? "—" : companies.length}</strong><span className="stat-card__detail">Persisted in the backend</span></div>
        <div className="stat-card"><span className="stat-card__label">Profile data</span><strong>—</strong><span className="stat-card__detail">Not available in the current API</span></div>
      </section>

      <div className="dashboard-columns">
        <Panel title="Recently updated" eyebrow="COMPANY LIST" className="dashboard-recent">
          {error ? <div className="empty-state"><strong>Could not load companies.</strong><p>{error}</p></div> : null}
          {!error && !loading && recentCompanies.length === 0 ? <div className="empty-state"><strong>No companies yet.</strong><p>Create a company identity to see it here.</p></div> : null}
          {!error && loading ? <div className="empty-state"><strong>Loading companies…</strong><p>Fetching records from the backend.</p></div> : null}
          {!error && !loading && recentCompanies.length ? <div className="company-preview-list">
            {recentCompanies.map((company) => (
              <Link className="company-preview" key={company.id} to={`/companies/${company.id}`}>
                <span className="company-avatar" aria-hidden="true">{getInitials(company.name)}</span>
                <span className="company-preview__main"><strong>{company.name}</strong><small>{company.country || "Country not provided"}</small></span>
                <span className="status">Identity</span>
              </Link>
            ))}
          </div> : null}
          <Link className="text-link" to="/companies">See all companies →</Link>
        </Panel>

        <Panel title="Workspace activity" eyebrow="OVERVIEW" className="activity-panel">
          <div className="empty-state"><strong>No activity endpoint yet.</strong><p>Research activity will appear here when the backend exposes it.</p></div>
        </Panel>
      </div>
    </div>
  );
}
