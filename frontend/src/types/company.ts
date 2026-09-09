export interface Company {
  id: string;
  name: string;
  website?: string | null;
  country?: string | null;
  industry?: string | null;
  status?: "Ready" | "Researching" | "Needs review" | "Draft" | null;
  logo?: string | null;
  headquarters?: string | null;
  employees?: string | null;
  linkedinUrl?: string | null;
  summary?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateCompanyRequest {
  name: string;
  website?: string;
  country?: string;
}
