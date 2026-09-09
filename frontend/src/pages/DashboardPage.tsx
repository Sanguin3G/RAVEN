import { Link } from "react-router-dom";
import { Panel } from "../components/Panel";
import { mockCompanies } from "../data/mockCompanies";

const statusCounts = {
  tracked: mockCompanies.length,
  ready: mockCompanies.filter((company) => company.status === "Ready").length,
  researching: mockCompanies.filter((company) => company.status === "Researching").length,
};

export function DashboardPage() {
  const recentCompanies = [...mockCompanies].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)).slice(0, 4);

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
        <div className="stat-card"><span className="stat-card__label">Tracked companies</span><strong>{statusCounts.tracked}</strong><span className="stat-card__detail">Across your workspace</span></div>
        <div className="stat-card"><span className="stat-card__label">Ready profiles</span><strong>{statusCounts.ready}</strong><span className="stat-card__detail">Profile data available</span></div>
        <div className="stat-card"><span className="stat-card__label">Research in progress</span><strong>{statusCounts.researching}</strong><span className="stat-card__detail">Awaiting source review</span></div>
      </section>

      <div className="dashboard-columns">
        <Panel title="Recently updated" eyebrow="COMPANY LIST" className="dashboard-recent">
          <div className="company-preview-list">
            {recentCompanies.map((company) => (
              <Link className="company-preview" key={company.id} to={`/companies/${company.id}`}>
                <span className="company-avatar" aria-hidden="true">{company.logo}</span>
                <span className="company-preview__main"><strong>{company.name}</strong><small>{company.industry} · {company.country}</small></span>
                <span className={`status status--${company.status?.toLowerCase().replaceAll(" ", "-")}`}>{company.status}</span>
              </Link>
            ))}
          </div>
          <Link className="text-link" to="/companies">See all companies →</Link>
        </Panel>

        <Panel title="Workspace activity" eyebrow="OVERVIEW" className="activity-panel">
          <div className="activity-item"><span className="activity-dot activity-dot--success" /><span><strong>Profile ready</strong><small>FPT Software profile is ready to review</small></span><time>Today</time></div>
          <div className="activity-item"><span className="activity-dot activity-dot--ai" /><span><strong>Research started</strong><small>VNG Corporation is being researched</small></span><time>Yesterday</time></div>
          <div className="activity-item"><span className="activity-dot activity-dot--warning" /><span><strong>Review needed</strong><small>Masan Group has new company signals</small></span><time>Sep 04</time></div>
        </Panel>
      </div>
    </div>
  );
}
