import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { Panel } from "../components/Panel";
import { getApiErrorMessage } from "../api/client";
import { getCompanies } from "../api/companies";
import type { Company } from "../types/company";

function getInitials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]).join("").toUpperCase() || "?";
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric" }).format(new Date(value));
}

export function CompanyListPage() {
  const [companies, setCompanies] = useState<Company[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [country, setCountry] = useState("");
  const [updatedAfter, setUpdatedAfter] = useState("");

  useEffect(() => {
    let active = true;
    getCompanies()
      .then((result) => { if (active) setCompanies(result); })
      .catch((reason: unknown) => { if (active) setError(getApiErrorMessage(reason, "Could not load companies.")); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, []);

  const countries = useMemo(() => [...new Set(companies.map((company) => company.country).filter(Boolean))] as string[], [companies]);

  const filteredCompanies = useMemo(() => companies.filter((company) => {
    const query = search.trim().toLowerCase();
    const matchesSearch = !query || [company.name, company.website].some((value) => value?.toLowerCase().includes(query));
    const matchesUpdated = !updatedAfter || company.updatedAt.slice(0, 10) >= updatedAfter;
    return matchesSearch && (!country || company.country === country) && matchesUpdated;
  }), [companies, country, search, updatedAfter]);

  function clearFilters() {
    setSearch(""); setCountry(""); setUpdatedAfter("");
  }

  return (
    <div className="page-stack">
      <div className="page-title-row">
        <div><p className="eyebrow">COMPANY DIRECTORY</p><h1>Company List</h1><p className="page-intro">Browse, filter and open the profiles in your workspace.</p></div>
        <Link className="button" to="/companies/new">+ Add Company Profile</Link>
      </div>

      <Panel className="directory-panel">
        <div className="filter-toolbar" aria-label="Company filters">
          <label className="filter-search"><span>Search company</span><input className="text-input" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search by company name" /></label>
          <label className="filter-field"><span>Country</span><select className="text-input" value={country} onChange={(event) => setCountry(event.target.value)}><option value="">All countries</option>{countries.map((item) => <option key={item}>{item}</option>)}</select></label>
          <label className="filter-field"><span>Updated after</span><input className="text-input" type="date" value={updatedAfter} onChange={(event) => setUpdatedAfter(event.target.value)} /></label>
          <button className="button button--quiet filter-clear" type="button" onClick={clearFilters}>Clear</button>
        </div>
        <div className="list-summary"><strong>{filteredCompanies.length} companies</strong><span>Showing results from the RAVEN API</span></div>
        {loading ? <div className="empty-state"><strong>Loading companies…</strong><p>Fetching company records from the backend.</p></div> : error ? <div className="empty-state"><strong>Could not load companies.</strong><p>{error}</p><button className="button button--secondary" type="button" onClick={() => window.location.reload()}>Retry</button></div> : filteredCompanies.length ? <div className="table-wrap"><table className="company-table"><thead><tr><th scope="col">Logo</th><th scope="col">Name</th><th scope="col">Country</th><th scope="col">Last update</th><th scope="col">Action</th></tr></thead><tbody>{filteredCompanies.map((company) => <tr key={company.id}><td><span className="company-avatar company-avatar--table" aria-hidden="true">{getInitials(company.name)}</span></td><th scope="row"><Link className="company-link" to={`/companies/${company.id}`}>{company.name}</Link><small className="table-secondary">Company identity</small></th><td>{company.country || "Not provided"}</td><td>{formatDate(company.updatedAt)}</td><td><Link className="action-link" to={`/companies/${company.id}`}>View detail →</Link></td></tr>)}</tbody></table></div> : <div className="empty-state"><strong>No companies match these filters.</strong><p>Try clearing a filter or search another company.</p><button className="button button--secondary" type="button" onClick={clearFilters}>Clear filters</button></div>}
      </Panel>
    </div>
  );
}
