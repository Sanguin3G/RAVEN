import type { Company, CreateCompanyRequest } from "../types/company";
import { request } from "./client";

export function getCompanies() {
  return request<Company[]>("/api/companies");
}

export function getCompany(id: string) {
  return request<Company>(`/api/companies/${encodeURIComponent(id)}`);
}

export function createCompany(company: CreateCompanyRequest) {
  return request<Company>("/api/companies", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(company),
  });
}
