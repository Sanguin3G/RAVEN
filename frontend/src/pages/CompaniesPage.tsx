import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getCompanies } from "../api/companies";
import { getApiErrorMessage } from "../api/client";
import { Panel } from "../components/Panel";
import { displayValue, displayWebsite, getWebsiteUrl } from "../features/companies/companyPresentation";
import type { Company } from "../types/company";

export function CompaniesPage() {
  const [companies, setCompanies] = useState<Company[] | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    let current = true;
    getCompanies()
      .then((result) => { if (current) setCompanies(result); })
      .catch((requestError: unknown) => { if (current) setError(getApiErrorMessage(requestError, "Could not load companies.")); });
    return () => { current = false; };
  }, []);

  return (
    <div className="page-stack">
      <div className="page-title-row">
        <div>
          <p className="eyebrow">COMPANY DIRECTORY</p>
          <h1>Companies under observation</h1>
        </div>
        <Link className="button button--primary" to="/companies/new">Add company</Link>
      </div>

      <Panel className="company-directory">
        {companies === null && !error ? <p className="state-message">Loading companies…</p> : null}
        {error ? <p className="state-message state-message--error" role="alert">{error}</p> : null}
        {companies?.length === 0 ? (
          <div className="empty-state">
            <p>No companies under observation yet.</p>
            <Link className="button button--secondary" to="/">Research a Company</Link>
          </div>
        ) : null}
        {companies?.length ? (
          <div className="table-wrap">
            <table>
              <thead><tr><th scope="col">Company</th><th scope="col">Country</th><th scope="col">Website</th></tr></thead>
              <tbody>
                {companies.map((company) => {
                  const websiteUrl = getWebsiteUrl(company.website);
                  return (
                    <tr key={company.id}>
                      <th scope="row"><Link className="company-link" to={`/companies/${company.id}`}>{company.name}</Link></th>
                      <td>{displayValue(company.country)}</td>
                      <td>{websiteUrl ? <a href={websiteUrl} target="_blank" rel="noreferrer">{displayWebsite(company.website)}</a> : "—"}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        ) : null}
      </Panel>
    </div>
  );
}
