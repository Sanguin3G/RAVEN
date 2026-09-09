import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { Button } from "../components/Button";
import { Panel } from "../components/Panel";
import { TextInput } from "../components/TextInput";
import { saveMockCompany, searchMockCompanies } from "../data/mockCompanies";
import type { Company } from "../types/company";

export function AddCompanyProfilePage() {
  const navigate = useNavigate();
  const [form, setForm] = useState({ name: "", website: "", country: "", industry: "", linkedin: "", headquarters: "" });
  const [results, setResults] = useState<Company[] | null>(null);
  const [selected, setSelected] = useState<Company | null>(null);
  const [searched, setSearched] = useState(false);

  function updateField(field: keyof typeof form, value: string) {
    setForm((current) => ({ ...current, [field]: value }));
  }

  function searchCompanies(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSearched(true);
    setResults(searchMockCompanies(form.name, form.country, form.industry));
  }

  return (
    <div className="page-stack add-profile-page">
      <div className="page-title-row"><div><p className="eyebrow">COMPANY DIRECTORY</p><h1>Add Company Profile</h1><p className="page-intro">Tell RAVEN what you know. We will find the closest public company records before you create a profile.</p></div><Link className="button button--secondary" to="/companies">Back to Company List</Link></div>
      <div className="add-profile-layout">
        <Panel title="Company information" eyebrow="STEP 01 · IDENTIFY" className="company-form-panel">
          <form onSubmit={searchCompanies} noValidate>
            <div className="form-grid">
              <TextInput label="Company name" id="add-name" name="name" value={form.name} onChange={(event) => updateField("name", event.target.value)} placeholder="e.g. FPT Software" required />
              <TextInput label="Website" id="add-website" name="website" type="url" value={form.website} onChange={(event) => updateField("website", event.target.value)} placeholder="https://example.com" />
              <TextInput label="Country" id="add-country" name="country" value={form.country} onChange={(event) => updateField("country", event.target.value)} placeholder="e.g. Vietnam" />
              <div className="field"><label htmlFor="add-industry">Industry</label><select id="add-industry" className="text-input" value={form.industry} onChange={(event) => updateField("industry", event.target.value)}><option value="">Select an industry</option><option>Technology</option><option>Consumer Goods</option><option>Mobility &amp; Fintech</option><option>Industrial Technology</option></select></div>
              <TextInput label="LinkedIn URL" id="add-linkedin" name="linkedin" type="url" value={form.linkedin} onChange={(event) => updateField("linkedin", event.target.value)} placeholder="https://linkedin.com/company/..." />
              <TextInput label="Headquarters" id="add-headquarters" name="headquarters" value={form.headquarters} onChange={(event) => updateField("headquarters", event.target.value)} placeholder="City, country" />
            </div>
            <div className="field"><label htmlFor="company-notes">Additional identifiers</label><p className="field__hint">Add a registration number, product, or other detail to improve matching.</p><textarea id="company-notes" className="text-input text-area" placeholder="Anything that helps identify the right company..." /></div>
            <div className="form-actions"><Button type="submit">Search matching companies</Button><Link className="button button--quiet" to="/companies">Cancel</Link></div>
          </form>
        </Panel>
        <Panel title="Matching companies" eyebrow="STEP 02 · SELECT" className="matching-panel">
          {!searched ? <div className="matching-placeholder"><span className="matching-placeholder__icon">⌕</span><strong>Search to see company matches</strong><p>Results will be matched against name, website, country and industry.</p></div> : null}
          {searched && results?.length === 0 ? <div className="empty-state"><strong>No close matches found.</strong><p>You can broaden the search or add a new profile manually.</p><button type="button" className="button button--secondary" onClick={() => { const draft = { id: "new-company", name: form.name || "New company", website: form.website || null, country: form.country || null, industry: form.industry || null, linkedinUrl: form.linkedin || null, headquarters: form.headquarters || null, status: "Draft" as const, logo: (form.name || "NC").slice(0, 2).toUpperCase(), summary: null, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString() }; saveMockCompany(draft); setSelected(draft); }}>Review entered company</button></div> : null}
          {results?.length ? <div className="match-list">{results.map((company) => <button type="button" className="match-card" key={company.id} onClick={() => setSelected(company)}><span className="company-avatar" aria-hidden="true">{company.logo}</span><span className="match-card__body"><strong>{company.name}</strong><small>{company.industry} · {company.country}</small><small>{company.website}</small></span><span className="match-card__arrow">→</span></button>)}</div> : null}
        </Panel>
      </div>
      {selected ? <div className="modal-backdrop" role="presentation" onMouseDown={(event) => { if (event.target === event.currentTarget) setSelected(null); }}><section className="match-modal" role="dialog" aria-modal="true" aria-labelledby="match-title"><button className="modal-close" type="button" aria-label="Close" onClick={() => setSelected(null)}>×</button><p className="eyebrow">COMPANY MATCH</p><div className="modal-company-heading"><span className="company-avatar company-avatar--large" aria-hidden="true">{selected.logo}</span><div><h2 id="match-title">{selected.name}</h2><p>{selected.industry || "Industry not provided"} · {selected.country || "Country not provided"}</p></div></div><dl className="definition-list modal-details"><div><dt>Website</dt><dd>{selected.website || "Not provided"}</dd></div><div><dt>Headquarters</dt><dd>{selected.headquarters || "Not provided"}</dd></div><div><dt>LinkedIn</dt><dd>{selected.linkedinUrl || "Not provided"}</dd></div><div><dt>Why this match</dt><dd>Name, domain and public company signals align with your search.</dd></div></dl><div className="modal-actions"><button type="button" className="button" onClick={() => navigate(`/companies/${selected.id}?generated=true`)}>Auto Generate Profile</button><button type="button" className="button button--secondary" onClick={() => navigate(`/companies/${selected.id}?manual=true`)}>Manually Create Profile</button></div></section></div> : null}
    </div>
  );
}
