import type { Company, CompanyMatchRequest, CompanyMatchResponse, CreateCompanyRequest } from "../types/company";
import { request } from "./client";

export function getCompanies() {
  return request<Company[]>("/api/companies");
}

export async function searchCompanies(query: string, country?: string) {
  const companies = await getCompanies();
  const normalizedQuery = query.trim().toLocaleLowerCase();
  const normalizedCountry = country?.trim().toLocaleLowerCase();

  return companies.filter((company) => {
    const matchesQuery = !normalizedQuery || [company.name, company.website]
      .some((value) => value?.toLocaleLowerCase().includes(normalizedQuery));
    const matchesCountry = !normalizedCountry || company.country?.toLocaleLowerCase() === normalizedCountry;
    return matchesQuery && matchesCountry;
  });
}

export function getCompany(id: string) {
  return request<Company>(`/api/companies/${encodeURIComponent(id)}`);
}

export function findCompanyMatches(identity: CompanyMatchRequest) {
  return request<CompanyMatchResponse[]>("/api/companies/matches", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(identity),
  });
}

export function createCompany(company: CreateCompanyRequest) {
  return request<Company>("/api/companies", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(company),
  });
}
