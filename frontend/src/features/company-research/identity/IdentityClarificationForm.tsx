import type { FormEvent } from "react";
import { Button } from "../../../components/Button";
import { TextInput } from "../../../components/TextInput";
import type {
  IdentityHintKind,
  IdentityInputField,
  IdentityResolutionRequest,
} from "../../../types/identity";
import styles from "./identity.module.css";

interface IdentityClarificationFormProps {
  input: IdentityResolutionRequest;
  requestedHints: IdentityHintKind[];
  onChange: (field: IdentityInputField, value: string) => void;
  onSubmit: (event: FormEvent<HTMLFormElement>) => void | Promise<void>;
  onManualExactName?: () => void;
  loading?: boolean;
  error?: string | null;
  message?: string | null;
  showName?: boolean;
  guided?: boolean;
}

const hintFields: Record<IdentityHintKind, {
  field: IdentityInputField;
  label: string;
  placeholder: string;
  autoComplete?: string;
  type?: "text" | "url";
}> = {
  Country: { field: "country", label: "Country", placeholder: "e.g. Vietnam", autoComplete: "country-name" },
  Website: { field: "website", label: "Website", placeholder: "https://example.com", autoComplete: "url", type: "url" },
  LegalName: { field: "legalName", label: "Legal name", placeholder: "Registered organization name", autoComplete: "organization" },
  RegistrationNumber: { field: "registrationNumber", label: "Registration / tax ID", placeholder: "Identifier and jurisdiction" },
  Headquarters: { field: "headquarters", label: "Headquarters", placeholder: "City, region, or address", autoComplete: "street-address" },
  // The preflight request carries geographic context as headquarters; the
  // label can stay narrower when the policy specifically asks for a region.
  Region: { field: "headquarters", label: "Region", placeholder: "e.g. Massachusetts" },
  ResearchHint: { field: "researchHint", label: "What the company does", placeholder: "e.g. telecom equipment manufacturer" },
};

function uniqueHints(requestedHints: IdentityHintKind[]) {
  const hints = requestedHints.length > 0 ? requestedHints : ["Country", "Website"] as IdentityHintKind[];
  return [...new Set(hints)].filter((hint) => hintFields[hint]);
}

export function IdentityClarificationForm({
  input,
  requestedHints,
  onChange,
  onSubmit,
  onManualExactName,
  loading = false,
  error,
  message,
  showName = true,
  guided = false,
}: IdentityClarificationFormProps) {
  const fields = (guided
    ? ["Country", "Website", "LegalName", "RegistrationNumber", "Headquarters", "ResearchHint"] as IdentityHintKind[]
    : uniqueHints(requestedHints)).filter((hint, index, all) =>
    all.findIndex((candidate) => hintFields[candidate].field === hintFields[hint].field) === index,
  );

  return (
    <form className={styles.clarification} onSubmit={onSubmit}>
      <div className={styles.clarificationHeading}>
        <strong>{message || "A little more information will help"}</strong>
        <p>Add only the detail that best distinguishes the organization. Your existing details will stay in place while you try again.</p>
      </div>
      <div className={styles.hintFields}>
        {showName ? (
          <TextInput
            id="identity-name"
            name="name"
            label="Company name"
            value={input.name}
            onChange={(event) => onChange("name", event.target.value)}
            autoComplete="organization"
            placeholder="e.g. FPT Software"
            required
            disabled={loading}
          />
        ) : null}
        {fields.filter((hint) => hint !== "ResearchHint").map((hint) => {
          const config = hintFields[hint];
          return (
            <TextInput
              key={hint}
              id={`identity-${config.field}`}
              name={config.field}
              label={config.label}
              value={String(input[config.field] ?? "")}
              onChange={(event) => onChange(config.field, event.target.value)}
              autoComplete={config.autoComplete}
              placeholder={config.placeholder}
              type={config.type}
              disabled={loading}
            />
          );
        })}
        {fields.includes("ResearchHint") ? (
          <div className="field">
            <label htmlFor="identity-research-hint">What the company does</label>
            <textarea id="identity-research-hint" name="researchHint" value={input.researchHint ?? ""} onChange={(event) => onChange("researchHint", event.target.value)} placeholder="e.g. telecom equipment manufacturer" maxLength={500} rows={3} disabled={loading} />
            <p className="field__hint">A short description can distinguish a subsidiary from its parent or peers.</p>
          </div>
        ) : null}
      </div>
      {error ? <p className={styles.error} role="alert">{error}</p> : null}
      <div className={styles.actions}>
        <Button type="submit" loading={loading}>{guided ? "Update company choices" : "Try again"}</Button>
        {onManualExactName ? (
          <Button type="button" tone="secondary" onClick={onManualExactName} disabled={loading || !input.name.trim()}>
            Research this exact name anyway
          </Button>
        ) : null}
      </div>
    </form>
  );
}
