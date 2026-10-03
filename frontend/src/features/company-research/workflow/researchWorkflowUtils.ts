import type { CreateCompanyRequest } from "../../../types/company";
import type { IdentityForm } from "../types";

export const initialForm: IdentityForm = {
  name: "",
  legalName: "",
  website: "",
  country: "",
  registrationNumber: "",
  headquarters: "",
  researchHint: "",
};

export function optional(value: string) {
  const trimmed = value.trim();
  return trimmed ? trimmed : undefined;
}

export function companyRequest(form: IdentityForm): CreateCompanyRequest {
  return {
    name: form.name.trim(),
    legalName: optional(form.legalName),
    website: optional(form.website),
    country: optional(form.country),
    registrationNumber: optional(form.registrationNumber),
    headquarters: optional(form.headquarters),
  };
}
