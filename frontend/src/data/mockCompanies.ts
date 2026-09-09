import type { Company } from "../types/company";

export const mockCompanies: Company[] = [
  {
    id: "fpt-software",
    name: "FPT Software",
    website: "https://fptsoftware.com",
    country: "Vietnam",
    industry: "Technology",
    status: "Ready",
    logo: "FS",
    headquarters: "Hanoi, Vietnam",
    employees: "10,000+",
    linkedinUrl: "https://www.linkedin.com/company/fpt-software/",
    summary: "Global technology services company delivering digital transformation and software solutions.",
    createdAt: "2026-09-01T00:00:00Z",
    updatedAt: "2026-09-08T08:30:00Z",
  },
  {
    id: "vng-corporation",
    name: "VNG Corporation",
    website: "https://vng.com.vn",
    country: "Vietnam",
    industry: "Technology",
    status: "Researching",
    logo: "VNG",
    headquarters: "Ho Chi Minh City, Vietnam",
    employees: "1,000–5,000",
    linkedinUrl: "https://www.linkedin.com/company/vng-corporation/",
    summary: "Vietnamese technology company focused on digital products, games and online services.",
    createdAt: "2026-09-03T00:00:00Z",
    updatedAt: "2026-09-07T14:10:00Z",
  },
  {
    id: "microsoft-vietnam",
    name: "Microsoft Vietnam",
    website: "https://www.microsoft.com/en-us/about",
    country: "Vietnam",
    industry: "Technology",
    status: "Ready",
    logo: "MS",
    headquarters: "Ho Chi Minh City, Vietnam",
    employees: "1,000+",
    linkedinUrl: "https://www.linkedin.com/company/microsoft/",
    summary: "Technology company providing cloud, productivity and business software platforms.",
    createdAt: "2026-08-26T00:00:00Z",
    updatedAt: "2026-09-06T09:45:00Z",
  },
  {
    id: "masan-group",
    name: "Masan Group",
    website: "https://www.masangroup.com",
    country: "Vietnam",
    industry: "Consumer Goods",
    status: "Needs review",
    logo: "MG",
    headquarters: "Ho Chi Minh City, Vietnam",
    employees: "5,000–10,000",
    linkedinUrl: "https://www.linkedin.com/company/masan-group/",
    summary: "Consumer-focused group building brands and services for Vietnamese households.",
    createdAt: "2026-08-21T00:00:00Z",
    updatedAt: "2026-09-04T16:20:00Z",
  },
  {
    id: "grab-vietnam",
    name: "Grab Vietnam",
    website: "https://www.grab.com/vn",
    country: "Singapore",
    industry: "Mobility & Fintech",
    status: "Draft",
    logo: "G",
    headquarters: "Singapore",
    employees: "10,000+",
    linkedinUrl: "https://www.linkedin.com/company/grabapp/",
    summary: "Technology platform offering mobility, delivery and digital financial services across Southeast Asia.",
    createdAt: "2026-08-12T00:00:00Z",
    updatedAt: "2026-09-02T11:05:00Z",
  },
  {
    id: "bosch-vietnam",
    name: "Bosch Vietnam",
    website: "https://www.bosch.com.vn",
    country: "Germany",
    industry: "Industrial Technology",
    status: "Ready",
    logo: "B",
    headquarters: "Ho Chi Minh City, Vietnam",
    employees: "5,000–10,000",
    linkedinUrl: "https://www.linkedin.com/company/robert-bosch-gmbh/",
    summary: "Engineering and technology company serving mobility, industrial and consumer markets.",
    createdAt: "2026-08-09T00:00:00Z",
    updatedAt: "2026-08-30T13:35:00Z",
  },
];

export function getMockCompany(id: string) {
  const stored = typeof localStorage !== "undefined" ? localStorage.getItem("raven-mock-companies") : null;
  const customCompanies: Company[] = stored ? JSON.parse(stored) as Company[] : [];
  return [...customCompanies, ...mockCompanies].find((company) => company.id === id) ?? null;
}

export function saveMockCompany(company: Company) {
  const stored = typeof localStorage !== "undefined" ? localStorage.getItem("raven-mock-companies") : null;
  const customCompanies: Company[] = stored ? JSON.parse(stored) as Company[] : [];
  localStorage.setItem("raven-mock-companies", JSON.stringify([company, ...customCompanies.filter((item) => item.id !== company.id)]));
}

export function searchMockCompanies(query: string, country?: string, industry?: string) {
  const normalizedQuery = query.trim().toLowerCase();
  return mockCompanies.filter((company) => {
    const matchesQuery = !normalizedQuery || [company.name, company.website, company.industry, company.country]
      .some((value) => value?.toLowerCase().includes(normalizedQuery));
    const matchesCountry = !country || company.country === country;
    const matchesIndustry = !industry || company.industry === industry;
    return matchesQuery && matchesCountry && matchesIndustry;
  });
}
