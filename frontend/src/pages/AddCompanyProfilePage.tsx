import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { Button } from "../components/Button";
import { Panel } from "../components/Panel";
import { TextInput } from "../components/TextInput";
import { getApiErrorMessage } from "../api/client";
import { createCompany } from "../api/companies";
import { startResearch } from "../api/research";

export function AddCompanyProfilePage() {
  const navigate = useNavigate();
  const [form, setForm] = useState({ name: "", website: "", country: "" });
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  function updateField(field: keyof typeof form, value: string) {
    setForm((current) => ({ ...current, [field]: value }));
  }

  async function startCompanyResearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setLoading(true);

    try {
      const company = await createCompany({
        name: form.name.trim(),
        website: form.website.trim() || undefined,
        country: form.country.trim() || undefined,
      });
      const researchRun = await startResearch(company.id);
      navigate(`/companies/${company.id}?researchRun=${encodeURIComponent(researchRun.id)}`);
    } catch (reason: unknown) {
      setError(getApiErrorMessage(reason, "RAVEN could not start public-source research."));
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="page-stack add-profile-page">
      <div className="page-title-row">
        <div>
          <p className="eyebrow">PUBLIC-SOURCE DISCOVERY</p>
          <h1>Research a company</h1>
          <p className="page-intro">Enter a company identity and RAVEN will search public sources, acquire the most relevant pages, and preserve them as evidence.</p>
        </div>
        <Link className="button button--secondary" to="/companies">Back to Company List</Link>
      </div>
      <div className="add-profile-layout">
        <Panel title="Company identity" eyebrow="STEP 01 · IDENTIFY" className="company-form-panel">
          <form onSubmit={startCompanyResearch}>
            <div className="form-grid">
              <TextInput label="Company name" id="add-name" name="name" autoComplete="organization" value={form.name} onChange={(event) => updateField("name", event.target.value)} placeholder="e.g. FPT Software" required />
              <TextInput label="Website" id="add-website" name="website" type="url" autoComplete="url" value={form.website} onChange={(event) => updateField("website", event.target.value)} placeholder="https://example.com" />
              <TextInput label="Country" id="add-country" name="country" autoComplete="country-name" value={form.country} onChange={(event) => updateField("country", event.target.value)} placeholder="e.g. Vietnam" />
            </div>
            <p className="field__hint">Company name is required. Website and country help RAVEN issue a more precise public-source search.</p>
            {error ? <p className="form-error" role="alert">{error}</p> : null}
            <div className="form-actions">
              <Button type="submit" loading={loading}>Research public sources</Button>
              <Link className="button button--quiet" to="/companies">Cancel</Link>
            </div>
          </form>
        </Panel>
        <Panel title="What RAVEN will do" eyebrow="STEP 02 · DISCOVER" className="matching-panel">
          <div className="matching-placeholder" aria-live="polite">
            <span className="matching-placeholder__icon" aria-hidden="true">⌕</span>
            {loading
              ? <><strong>Researching public sources…</strong><p>RAVEN is searching with Brave and acquiring selected pages with Crawl4AI Local.</p></>
              : <><strong>Evidence-backed research</strong><p>RAVEN will save the company identity, discover relevant public sources, and store acquired content with the research run.</p></>}
          </div>
        </Panel>
      </div>
    </div>
  );
}
