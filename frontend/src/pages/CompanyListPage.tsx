import { useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { Panel } from "../components/Panel";
import { mockCompanies } from "../data/mockCompanies";

const industries = [...new Set(mockCompanies.map((company) => company.industry).filter(Boolean))] as string[];
const countries = [...new Set(mockCompanies.map((company) => company.country).filter(Boolean))] as string[];
const statuses = [...new Set(mockCompanies.map((company) => company.status).filter(Boolean))] as string[];

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "short", year: "numeric" }).format(new Date(value));
}

export function CompanyListPage() {
  const [search, setSearch] = useState("");
  const [industry, setIndustry] = useState("");
  const [country, setCountry] = useState("");
  const [status, setStatus] = useState("");
  const [updatedAfter, setUpdatedAfter] = useState("");

  const filteredCompanies = useMemo(() => mockCompanies.filter((company) => {
    const query = search.trim().toLowerCase();
    const matchesSearch = !query || company.name.toLowerCase().includes(query);
    const matchesUpdated = !updatedAfter || company.updatedAt.slice(0, 10) >= updatedAfter;
    return matchesSearch && (!industry || company.industry === industry) && (!country || company.country === country) && (!status || company.status === status) && matchesUpdated;
  }), [country, industry, search, status, updatedAfter]);

  function clearFilters() {
    setSearch(""); setIndustry(""); setCountry(""); setStatus(""); setUpdatedAfter("");
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
          <label className="filter-field"><span>Industry</span><select className="text-input" value={industry} onChange={(event) => setIndustry(event.target.value)}><option value="">All industries</option>{industries.map((item) => <option key={item}>{item}</option>)}</select></label>
          <label className="filter-field"><span>Country</span><select className="text-input" value={country} onChange={(event) => setCountry(event.target.value)}><option value="">All countries</option>{countries.map((item) => <option key={item}>{item}</option>)}</select></label>
          <label className="filter-field"><span>Status</span><select className="text-input" value={status} onChange={(event) => setStatus(event.target.value)}><option value="">All statuses</option>{statuses.map((item) => <option key={item}>{item}</option>)}</select></label>
          <label className="filter-field"><span>Updated after</span><input className="text-input" type="date" value={updatedAfter} onChange={(event) => setUpdatedAfter(event.target.value)} /></label>
          <button className="button button--quiet filter-clear" type="button" onClick={clearFilters}>Clear</button>
        </div>
        <div className="list-summary"><strong>{filteredCompanies.length} companies</strong><span>Showing results from your mock workspace</span></div>
        {filteredCompanies.length ? <div className="table-wrap"><table className="company-table"><thead><tr><th scope="col">Logo</th><th scope="col">Name</th><th scope="col">Country</th><th scope="col">Status</th><th scope="col">Last update</th><th scope="col">Action</th></tr></thead><tbody>{filteredCompanies.map((company) => <tr key={company.id}><td><span className="company-avatar company-avatar--table" aria-hidden="true">{company.logo}</span></td><th scope="row"><Link className="company-link" to={`/companies/${company.id}`}>{company.name}</Link><small className="table-secondary">{company.industry}</small></th><td>{company.country}</td><td><span className={`status status--${company.status?.toLowerCase().replaceAll(" ", "-")}`}>{company.status}</span></td><td>{formatDate(company.updatedAt)}</td><td><Link className="action-link" to={`/companies/${company.id}`}>View detail →</Link></td></tr>)}</tbody></table></div> : <div className="empty-state"><strong>No companies match these filters.</strong><p>Try clearing a filter or search another company.</p><button className="button button--secondary" type="button" onClick={clearFilters}>Clear filters</button></div>}
      </Panel>
    </div>
  );
}
