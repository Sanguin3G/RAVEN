export interface Company {
  id: string;
  name: string;
  website?: string | null;
  country?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateCompanyRequest {
  name: string;
  website?: string;
  country?: string;
}
