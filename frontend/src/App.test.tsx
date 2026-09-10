import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { App } from "./App";
import { jsonResponse, renderWithRouter } from "./test/test-utils";
import type { Company } from "./types/company";

const apiCompanies: Company[] = [
  { id: "11111111-1111-1111-1111-111111111111", name: "FPT Software", website: "https://fptsoftware.com", country: "Vietnam", createdAt: "2026-09-01T00:00:00Z", updatedAt: "2026-09-08T08:30:00Z" },
  { id: "22222222-2222-2222-2222-222222222222", name: "Masan Group", website: "https://www.masangroup.com", country: "Vietnam", createdAt: "2026-08-21T00:00:00Z", updatedAt: "2026-09-04T16:20:00Z" },
];

const researchRun = {
  id: "44444444-4444-4444-4444-444444444444",
  companyId: "33333333-3333-3333-3333-333333333333",
  status: "Completed",
  requestedSearchProvider: "brave",
  actualSearchProvider: "brave",
  requestedCrawlerProvider: "crawl4ai-local",
  actualCrawlerProvider: "crawl4ai-local",
  sourcesFound: 3,
  sourcesSelected: 2,
  sourcesCrawled: 2,
  startedAt: "2026-09-10T00:00:00Z",
  completedAt: "2026-09-10T00:01:00Z",
  error: null,
  stage: "AwaitingSourceSelection",
  researchHint: null,
  queriesTotal: 4,
  queriesCompleted: 4,
  uniqueCandidates: 2,
  recommendedCandidates: 2,
  crawlTotal: 0,
  crawlCompleted: 0,
  crawlSucceeded: 0,
  crawlFailed: 0,
  documentsAdded: 0,
  duplicatesSkipped: 0,
};

const acquiredResearchRun = {
  ...researchRun,
  status: "Completed",
  stage: "EvidenceReady",
  sourcesSelected: 2,
  sourcesCrawled: 2,
  crawlTotal: 2,
  crawlCompleted: 2,
  crawlSucceeded: 2,
  documentsAdded: 2,
};

const researchCandidates = [
  { id: "66666666-6666-6666-6666-666666666666", researchRunId: researchRun.id, url: "https://fptsoftware.com/about", normalizedUrl: "https://fptsoftware.com/about", domain: "fptsoftware.com", title: "About FPT Software", snippet: null, sourceKind: "OfficialWebsite", recommendationReasons: ["Official domain"], recommended: true, selected: false, acquisitionStatus: "Pending", acquisitionError: null, iconUrl: null, discoveredAt: "2026-09-10T00:00:00Z" },
  { id: "77777777-7777-7777-7777-777777777777", researchRunId: researchRun.id, url: "https://fptsoftware.com/services", normalizedUrl: "https://fptsoftware.com/services", domain: "fptsoftware.com", title: "FPT services", snippet: null, sourceKind: "OfficialWebsite", recommendationReasons: ["Official domain"], recommended: true, selected: false, acquisitionStatus: "Pending", acquisitionError: null, iconUrl: null, discoveredAt: "2026-09-10T00:00:00Z" },
];

const researchSources = [{
  id: "55555555-5555-5555-5555-555555555555",
  companyId: researchRun.companyId,
  researchRunId: researchRun.id,
  url: "https://fptsoftware.com/about",
  title: "About FPT Software",
  sourceDomain: "fptsoftware.com",
  sourceKind: "OfficialWebsite",
  iconUrl: null,
  retrievedAt: "2026-09-10T00:00:00Z",
  crawlerProvider: "crawl4ai-local",
  contentPreview: "FPT Software company information.",
}];

beforeEach(() => {
  localStorage.clear();
  let createdCompany: typeof apiCompanies[number] | null = null;
  vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
    const url = String(input);
    if (url.endsWith("/matches") && init?.method === "POST") return jsonResponse([]);
    if (url.endsWith("/profile")) return jsonResponse({ message: "No accepted profile" }, 404);
    if (url.endsWith("/research/discover") && init?.method === "POST") return jsonResponse(researchRun);
    if (url.endsWith("/candidates")) return jsonResponse(researchCandidates);
    if (url.endsWith("/acquire") && init?.method === "POST") return jsonResponse(acquiredResearchRun);
    if (url.endsWith("/sources")) return jsonResponse(researchSources);
    if (url.includes("/api/research-runs/")) return jsonResponse(researchRun);
    if (url.endsWith("/api/companies") && init?.method === "POST") {
      const body = JSON.parse(String(init.body)) as { name: string; website?: string; country?: string };
      const company = { id: researchRun.companyId, name: body.name, website: body.website || null, country: body.country || null, createdAt: "2026-09-10T00:00:00Z", updatedAt: "2026-09-10T00:00:00Z" };
      createdCompany = company;
      return jsonResponse(company, 201);
    }

    if (url.includes("/api/companies/") && createdCompany) return jsonResponse(createdCompany);
    if (url.includes("/api/companies/")) return jsonResponse(apiCompanies[0]);
    return jsonResponse(apiCompanies);
  });
});

it("renders the Day 2 dashboard and new navigation", async () => {
  renderWithRouter(<App />);

  expect(screen.getByRole("heading", { name: "Company intelligence, at a glance." })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Dashboard" })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Companies" })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Research Company" })).toBeInTheDocument();
  expect(screen.queryByText("Ask RAVEN")).not.toBeInTheDocument();
  await screen.findByText("FPT Software");
});

it("filters the company list by name", async () => {
  const user = userEvent.setup();
  renderWithRouter(<App />, "/companies");

  await screen.findByRole("link", { name: "FPT Software" });
  expect(screen.getByRole("columnheader", { name: "Logo" })).toBeInTheDocument();
  expect(screen.getByRole("columnheader", { name: "Last update" })).toBeInTheDocument();
  await user.type(screen.getByPlaceholderText("Search by company name"), "Masan");

  expect(screen.getByRole("link", { name: "Masan Group" })).toBeInTheDocument();
  expect(screen.queryByRole("link", { name: "FPT Software" })).not.toBeInTheDocument();
});

it("opens a company detail from the list", async () => {
  const user = userEvent.setup();
  renderWithRouter(<App />, "/companies");
  await screen.findByRole("link", { name: "FPT Software" });
  await user.click(screen.getByRole("link", { name: "FPT Software" }));

  expect(await screen.findByRole("heading", { name: "FPT Software" })).toBeInTheDocument();
  expect(screen.getByText("https://fptsoftware.com")).toBeInTheDocument();
});

it("runs staged public-source research and shows its acquired evidence", async () => {
  const user = userEvent.setup();
  renderWithRouter(<App />, "/companies/new");
  await user.type(screen.getByLabelText(/Company name/), "FPT Software");
  await user.click(screen.getByRole("button", { name: "Research public sources" }));

  expect(await screen.findByText(/Review source candidates/i)).toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: /Acquire 2 selected sources/i }));
  expect(await screen.findByText(/Evidence ready/i)).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Open source" })).toHaveAttribute("href", "https://fptsoftware.com/about");
  expect(globalThis.fetch).toHaveBeenCalledWith("/api/companies/33333333-3333-3333-3333-333333333333/research/discover", expect.objectContaining({ method: "POST" }));
});

it("marks only Research Company as active on the research page", () => {
  renderWithRouter(<App />, "/companies/new");

  expect(screen.getByRole("link", { name: "Research Company" })).toHaveClass("active");
  expect(screen.getByRole("link", { name: "Companies" })).not.toHaveClass("active");
});

it("switches and persists the selected theme", async () => {
  const user = userEvent.setup();
  const view = renderWithRouter(<App />, "/settings");
  await user.click(screen.getByRole("radio", { name: /Dark/i }));

  expect(document.documentElement.dataset.theme).toBe("dark");
  expect(localStorage.getItem("raven-theme-preference")).toBe("dark");
  view.unmount();
  renderWithRouter(<App />, "/settings");
  expect(screen.getByRole("radio", { name: /Dark/i })).toBeChecked();
});
