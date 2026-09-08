import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { getCompanies } from "../api/companies";
import { getApiErrorMessage } from "../api/client";
import { Button } from "../components/Button";
import { Panel } from "../components/Panel";
import { TextInput } from "../components/TextInput";
import type { Company } from "../types/company";

export function HomePage() {
  const navigate = useNavigate();
  const [companyName, setCompanyName] = useState("");
  const [validationError, setValidationError] = useState("");
  const [recentCompanies, setRecentCompanies] = useState<Company[] | null>(null);
  const [recentError, setRecentError] = useState("");

  useEffect(() => {
    let current = true;

    getCompanies()
      .then((companies) => {
        if (current) setRecentCompanies(companies.slice(0, 4));
      })
      .catch((error: unknown) => {
        if (current) setRecentError(getApiErrorMessage(error, "Recent companies are unavailable."));
      });

    return () => { current = false; };
  }, []);

  function continueToCompanyDetails(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const name = companyName.trim();

    if (!name) {
      setValidationError("Enter a company name to begin.");
      return;
    }

    navigate(`/companies/new?name=${encodeURIComponent(name)}`);
  }

  return (
    <div className="home-layout">
      <section className="home-hero" aria-labelledby="home-title">
        <p className="eyebrow">COMPANY INTELLIGENCE DESK</p>
        <h1 id="home-title">What company are we looking into today?</h1>
        <p className="home-hero__intro">Begin a company record now. Research and evidence will join this same workspace next.</p>
        <form className="research-entry" onSubmit={continueToCompanyDetails} noValidate>
          <TextInput
            label="Company name"
            name="company-name"
            value={companyName}
            onChange={(event) => {
              setCompanyName(event.target.value);
              if (validationError) setValidationError("");
            }}
            placeholder="FPT Software"
            autoComplete="organization"
            error={validationError}
          />
          <Button type="submit">Continue</Button>
        </form>
      </section>

      <Panel className="recent-panel" eyebrow="FIELD NOTES" title="Recently Investigated">
        {recentCompanies === null && !recentError ? <p className="state-message">Loading companies…</p> : null}
        {recentError ? <p className="state-message state-message--error">Recently investigated companies are unavailable while the API is offline.</p> : null}
        {recentCompanies?.length === 0 ? <p className="state-message">No companies under observation yet.</p> : null}
        {recentCompanies?.length ? (
          <ul className="recent-company-list">
            {recentCompanies.map((company) => <li key={company.id}><Link to={`/companies/${company.id}`}>{company.name}</Link></li>)}
          </ul>
        ) : null}
      </Panel>
    </div>
  );
}
