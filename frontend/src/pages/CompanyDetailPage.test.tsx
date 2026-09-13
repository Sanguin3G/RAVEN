import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { CompanyDetailPage } from "./CompanyDetailPage";
import { ThemeProvider } from "../app/theme";
import { jsonResponse } from "../test/test-utils";

const companyId = "44444444-4444-4444-4444-444444444444";

const company = {
  id: companyId,
  name: "Northwind Research",
  website: "https://northwind.example",
  country: "Vietnam",
  legalName: "Northwind Research Company",
  registrationNumber: null,
  headquarters: null,
  createdAt: "2026-09-01T00:00:00Z",
  updatedAt: "2026-09-10T00:00:00Z",
  lastResearchedAt: "2026-09-10T00:00:00Z",
  archivedAt: null,
};

const profile = {
  id: "profile-1",
  companyId,
  researchRunId: "run-1",
  generatedAt: "2026-09-10T00:00:00Z",
  confirmedAt: "2026-09-10T00:00:00Z",
  version: 1,
  legalName: company.legalName,
  website: company.website,
  country: company.country,
  headquarters: null,
  registrationNumberOrTaxId: null,
  foundedYear: null,
  primaryIndustry: null,
  secondaryIndustries: [],
  companySize: null,
  employeeCount: null,
  employeeCountRange: null,
  summary: "A research company.",
  productsServices: [],
  markets: [],
  leadership: [],
  locations: [],
  publicLinks: [],
  evidence: [],
  validationWarnings: [],
};

const monitoring = {
  companyId,
  enabled: false,
  cadence: "Weekly",
  nextRunAt: null,
  lastRunAt: null,
  lastRunStatus: null,
};

function installCompanyApi(profileStatus = 200) {
  vi.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
    // Keep this fixture compatible with both plain URL strings and Request
    // objects, which differ between jsdom/Vite fetch implementations.
    const url = input instanceof Request ? input.url : String(input);
    if (url.endsWith(`/api/companies/${companyId}/profile`)) return jsonResponse(profile, profileStatus);
    if (url.endsWith(`/api/companies/${companyId}/sources`)) return jsonResponse([]);
    if (url.endsWith(`/api/companies/${companyId}/profile/versions`)) return jsonResponse([profile]);
    if (url.endsWith(`/api/companies/${companyId}/profile/changes`)) return jsonResponse([]);
    if (url.endsWith(`/api/companies/${companyId}/coverage`)) return jsonResponse({ companyId, researchRunId: null, items: [], budgetExhausted: false });
    if (url.endsWith(`/api/companies/${companyId}/monitoring`)) return jsonResponse(monitoring);
    if (url.endsWith(`/api/companies/${companyId}`)) return jsonResponse(company);
    return jsonResponse([]);
  });
}

function renderCompanyDetail(route: string) {
  return render(
    <ThemeProvider>
      <MemoryRouter initialEntries={[route]}>
        <Routes><Route path="/companies/:id" element={<CompanyDetailPage />} /></Routes>
      </MemoryRouter>
    </ThemeProvider>,
  );
}

describe("CompanyDetailPage workspace actions", () => {
  beforeEach(() => {
    installCompanyApi();
  });

  it("opens the Monitoring dossier tab from the row-action URL", async () => {
    renderCompanyDetail(`/companies/${companyId}?tab=monitoring`);

    expect(await screen.findByRole("tab", { name: "Monitoring" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByTestId("company-monitoring-panel")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Monitor Northwind Research" })).toBeInTheDocument();
  });

  it("opens the targeted profile-improvement dialog from the row-action URL", async () => {
    renderCompanyDetail(`/companies/${companyId}?improve=true`);

    const dialog = await screen.findByRole("dialog");
    expect(dialog).toHaveTextContent("What should RAVEN strengthen?");
    expect(dialog).toHaveTextContent("Target-specific evidence only");
  });

  it("explains unavailable workspace actions for a company without an accepted profile", async () => {
    installCompanyApi(404);
    renderCompanyDetail(`/companies/${companyId}?improve=true`);

    expect(await screen.findByRole("heading", { name: "This profile is not ready to improve" })).toBeInTheDocument();
    expect(screen.getByText(/does not have an accepted Company Profile/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Refresh research" })).toHaveAttribute("href", `/companies/new?refreshCompanyId=${companyId}`);
    expect(screen.getByRole("link", { name: "Review workspace" })).toHaveAttribute("href", "/companies?review=true");
  });

  it("keeps tab navigation addressable and clears a pending improve intent", async () => {
    const user = (await import("@testing-library/user-event")).default.setup();
    renderCompanyDetail(`/companies/${companyId}?improve=true`);

    await screen.findByRole("dialog");
    await user.click(screen.getByRole("tab", { name: "Monitoring" }));
    await waitFor(() => expect(screen.getByRole("tab", { name: "Monitoring" })).toHaveAttribute("aria-selected", "true"));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
});
