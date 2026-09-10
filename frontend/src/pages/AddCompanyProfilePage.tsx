import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { Button } from "../components/Button";
import { Panel } from "../components/Panel";
import { TextInput } from "../components/TextInput";
import { getApiErrorMessage } from "../api/client";
import { createCompany, searchCompanies } from "../api/companies";
import type { Company } from "../types/company";

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en-GB", { day: "2-digit", month: "long", year: "numeric" }).format(new Date(value));
}

export function AddCompanyProfilePage() {
  const navigate = useNavigate();
  const [form, setForm] = useState({ name: "", website: "", country: "" });
  const [results, setResults] = useState<Company[] | null>(null);
  const [selected, setSelected] = useState<Company | null>(null);
  const [searched, setSearched] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function updateField(field: keyof typeof form, value: string) {
    setForm((current) => ({ ...current, [field]: value }));
  }

  async function searchMatchingCompanies(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSearched(true);
    setSelected(null);
    setError(null);
    setLoading(true);
    try {
      setResults(await searchCompanies(form.name, form.country));
    } catch (reason: unknown) {
      setResults(null);
      setError(getApiErrorMessage(reason, "Could not search companies."));
    } finally {
      setLoading(false);
    }
  }

  async function reviewEnteredCompany() {
    setError(null);
    setLoading(true);
    try {
      const company = await createCompany({ name: form.name, website: form.website || undefined, country: form.country || undefined });
      setSelected(company);
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "Could not create the company."));
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="page-stack add-profile-page">
      <div className="page-title-row"><div><p className="eyebrow">COMPANY DIRECTORY</p><h1>Add Company Profile</h1><p className="page-intro">Tell RAVEN what you know. We will find the closest public company records before you create a profile.</p></div><Link className="button button--secondary" to="/companies">Back to Company List</Link></div>
      <div className="add-profile-layout">
        <Panel title="Company information" eyebrow="STEP 01 · IDENTIFY" className="company-form-panel">
          <form onSubmit={searchMatchingCompanies} noValidate>
            <div className="form-grid">
              <TextInput label="Company name" id="add-name" name="name" value={form.name} onChange={(event) => updateField("name", event.target.value)} placeholder="e.g. FPT Software" required />
              <TextInput label="Website" id="add-website" name="website" type="url" value={form.website} onChange={(event) => updateField("website", event.target.value)} placeholder="https://example.com" />
              <TextInput label="Country" id="add-country" name="country" value={form.country} onChange={(event) => updateField("country", event.target.value)} placeholder="e.g. Vietnam" />
            </div>
            <p className="field__hint">The current backend stores the company name, website and country. Research and profile fields will be added after the company identity is created.</p>
            <div className="form-actions"><Button type="submit">Search matching companies</Button><Link className="button button--quiet" to="/companies">Cancel</Link></div>
          </form>
        </Panel>
        <Panel title="Matching companies" eyebrow="STEP 02 · SELECT" className="matching-panel">
          {!searched ? <div className="matching-placeholder"><span className="matching-placeholder__icon">⌕</span><strong>Search to see company matches</strong><p>Results are loaded from company identities stored by the RAVEN backend.</p></div> : null}
          {loading ? <div className="matching-placeholder"><strong>Searching the RAVEN API…</strong><p>Fetching company records from the backend.</p></div> : null}
          {error ? <div className="empty-state"><strong>Request failed.</strong><p>{error}</p></div> : null}
          {!loading && !error && searched && results?.length === 0 ? <div className="empty-state"><strong>No close matches found.</strong><p>You can add this company to the backend as a new identity.</p><button type="button" className="button button--secondary" onClick={reviewEnteredCompany}>Create company identity</button></div> : null}
          {!loading && results?.length ? <div className="match-list">{results.map((company) => <button type="button" className="match-card" key={company.id} onClick={() => setSelected(company)}><span className="company-avatar" aria-hidden="true">{company.name.slice(0, 2).toUpperCase()}</span><span className="match-card__body"><strong>{company.name}</strong><small>{company.country || "Country not provided"}</small><small>{company.website || "Website not provided"}</small></span><span className="match-card__arrow">→</span></button>)}</div> : null}
        </Panel>
      </div>
      {selected ? <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) setSelected(null); }}><section className="match-modal" role="dialog" aria-modal="true" aria-labelledby="match-title"><button className="modal-close" type="button" aria-label="Close" onClick={() => setSelected(null)}>×</button><p className="eyebrow">COMPANY MATCH</p><div className="modal-company-heading"><span className="company-avatar company-avatar--large" aria-hidden="true">{selected.name.slice(0, 2).toUpperCase()}</span><div><h2 id="match-title">{selected.name}</h2><p>{selected.country || "Country not provided"}</p></div></div><dl className="definition-list modal-details"><div><dt>Website</dt><dd>{selected.website || "Not provided"}</dd></div><div><dt>Company ID</dt><dd>{selected.id}</dd></div><div><dt>Created</dt><dd>{formatDate(selected.createdAt)}</dd></div><div><dt>Why this match</dt><dd>The record was returned by the RAVEN backend for your search.</dd></div></dl><div className="modal-actions"><button type="button" className="button" onClick={() => navigate(`/companies/${selected.id}?generated=true`)}>Auto Generate Profile</button><button type="button" className="button button--secondary" onClick={() => navigate(`/companies/${selected.id}?manual=true`)}>Manually Create Profile</button></div></section></div> : null}
    </div>
  );
}
