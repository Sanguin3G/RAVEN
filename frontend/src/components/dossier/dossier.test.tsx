import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { CompanyDossier } from "./CompanyDossier";
import type { DossierCompany, DossierProfile } from "./dossierTypes";

const company: DossierCompany = {
  id: "company-42",
  displayName: "Northwind Research",
  country: "Vietnam",
};

const sparseProfile: DossierProfile = {
  summary: null,
  foundedYear: null,
  productsServices: null,
  markets: [],
  leadership: null,
  locations: null,
  publicLinks: null,
};

function renderDossier(element: React.ReactElement) {
  return render(<MemoryRouter>{element}</MemoryRouter>);
}
describe("CompanyDossier", () => {
  it("renders null and missing profile fields as explicit unknowns", () => {
    renderDossier(<CompanyDossier company={company} profile={sparseProfile} />);

    expect(screen.getByTestId("dossier-overview")).toBeInTheDocument();
    expect(screen.getByText("Legal identity not verified")).toBeInTheDocument();
    expect(screen.getAllByText("Not verified").length).toBeGreaterThanOrEqual(4);
    expect(screen.getByText(/No supported products or services/)).toBeInTheDocument();
    expect(screen.getByText(/No explicitly associated leaders/)).toBeInTheDocument();
    expect(screen.getByText(/No operating locations/)).toBeInTheDocument();
  });

  it("switches tabs and supports keyboard tab navigation", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response("[]", { status: 200, headers: { "Content-Type": "application/json" } }));
    renderDossier(<CompanyDossier company={company} profile={sparseProfile} />);

    const sourcesTab = screen.getByRole("tab", { name: "Sources" });
    fireEvent.click(sourcesTab);
    expect(screen.getByRole("tabpanel")).toHaveAttribute("aria-labelledby", expect.stringContaining("tab-sources"));
    expect(screen.getByTestId("dossier-sources")).toBeInTheDocument();

    const investigationsTab = screen.getByRole("tab", { name: "Investigations" });
    fireEvent.keyDown(sourcesTab, { key: "ArrowRight" });
    expect(investigationsTab).toHaveFocus();
    expect(investigationsTab).toHaveAttribute("aria-selected", "true");
    await waitFor(() => expect(screen.getByTestId("dossier-investigations")).toBeInTheDocument());
    expect(screen.queryByRole("tab", { name: "Research" })).not.toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: "Ask RAVEN" })).not.toBeInTheDocument();
  });

  it("renders qualitative coverage without a numeric confidence score", () => {
    renderDossier(
      <CompanyDossier
        company={company}
        profile={sparseProfile}
        coverage={{
          response: {
            companyId: company.id,
            researchRunId: null,
            budgetExhausted: false,
            items: [{ target: "Leadership", level: "Missing", supportingSourceCount: 0, strongestSourceKind: null, reasons: ["No leadership evidence"] }],
          },
        }}
      />,
    );

    const coverage = screen.getByTestId("company-coverage");
    expect(coverage).toBeInTheDocument();
    const leadershipRow = within(coverage).getByText("Leadership").closest("li");
    expect(leadershipRow).not.toBeNull();
    expect(within(leadershipRow as HTMLElement).getByText("Missing")).toBeInTheDocument();
    expect(screen.queryByText(/^\d+%$/)).not.toBeInTheDocument();
  });
});
