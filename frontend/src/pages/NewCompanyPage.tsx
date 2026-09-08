import { useEffect, useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { createCompany } from "../api/companies";
import { getApiErrorMessage } from "../api/client";
import { Button } from "../components/Button";
import { Panel } from "../components/Panel";
import { TextInput } from "../components/TextInput";

interface FormErrors {
  name?: string;
}

export function NewCompanyPage() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [name, setName] = useState("");
  const [website, setWebsite] = useState("");
  const [country, setCountry] = useState("");
  const [errors, setErrors] = useState<FormErrors>({});
  const [submissionError, setSubmissionError] = useState("");
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    setName(searchParams.get("name") ?? "");
  }, [searchParams]);

  async function submitCompany(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmedName = name.trim();

    if (!trimmedName) {
      setErrors({ name: "Company name is required." });
      return;
    }

    setErrors({});
    setSubmissionError("");
    setSubmitting(true);

    try {
      const company = await createCompany({
        name: trimmedName,
        website: website.trim() || undefined,
        country: country.trim() || undefined,
      });
      navigate(`/companies/${company.id}`);
    } catch (error) {
      setSubmissionError(getApiErrorMessage(error, "RAVEN could not create this company. Please try again."));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="narrow-page page-stack">
      <div>
        <p className="eyebrow">COMPANY DIRECTORY</p>
        <h1>Company details</h1>
        <p className="page-intro">Start a record that RAVEN can grow into a source-grounded profile.</p>
      </div>

      <Panel title="Company Details" eyebrow="NEW RECORD">
        <form className="company-form" onSubmit={submitCompany} noValidate>
          <TextInput
            label="Company Name"
            id="name"
            name="name"
            value={name}
            onChange={(event) => {
              setName(event.target.value);
              if (errors.name) setErrors({});
            }}
            autoComplete="organization"
            required
            error={errors.name}
            autoFocus
          />
          <TextInput
            label="Website"
            id="website"
            name="website"
            type="url"
            value={website}
            onChange={(event) => setWebsite(event.target.value)}
            placeholder="https://example.com"
            autoComplete="url"
          />
          <TextInput
            label="Country"
            id="country"
            name="country"
            value={country}
            onChange={(event) => setCountry(event.target.value)}
            placeholder="Vietnam"
            autoComplete="country-name"
          />
          {submissionError ? <p className="form-error" role="alert">{submissionError}</p> : null}
          <div className="form-actions">
            <Button type="submit" loading={submitting}>Create company</Button>
            <Link className="button button--quiet" to="/companies">Cancel</Link>
          </div>
        </form>
      </Panel>
    </div>
  );
}
