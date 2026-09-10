import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
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

describe("CompanyDossier", () => {
  it("renders null and missing profile fields as explicit unknowns", () => {
    render(<CompanyDossier company={company} profile={sparseProfile} />);

    expect(screen.getByTestId("dossier-overview")).toBeInTheDocument();
    expect(screen.getByText("Legal identity not verified")).toBeInTheDocument();
    expect(screen.getAllByText("Not verified").length).toBeGreaterThanOrEqual(4);
    expect(screen.getByText(/No supported products or services/)).toBeInTheDocument();
    expect(screen.getByText(/No explicitly associated leaders/)).toBeInTheDocument();
    expect(screen.getByText(/No operating locations/)).toBeInTheDocument();
  });

  it("switches tabs and supports keyboard tab navigation", () => {
    render(<CompanyDossier company={company} profile={sparseProfile} />);

    const sourcesTab = screen.getByRole("tab", { name: "Sources" });
    fireEvent.click(sourcesTab);
    expect(screen.getByRole("tabpanel")).toHaveAttribute("aria-labelledby", expect.stringContaining("tab-sources"));
    expect(screen.getByTestId("dossier-sources")).toBeInTheDocument();

    const researchTab = screen.getByRole("tab", { name: "Research" });
    fireEvent.keyDown(sourcesTab, { key: "ArrowRight" });
    expect(researchTab).toHaveFocus();
    expect(researchTab).toHaveAttribute("aria-selected", "true");
    expect(screen.getByTestId("dossier-research-empty")).toBeInTheDocument();
  });
});
