import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { getInvestigationOrganization, getSavedInvestigations, organizeInvestigation } from "../../api/investigations";
import { getManagedResearchJobs } from "../../api/managedResearch";
import { CompanyInvestigationsTab } from "./CompanyInvestigationsTab";

vi.mock("../../api/investigations", () => ({
  getSavedInvestigations: vi.fn(),
  getInvestigationOrganization: vi.fn(),
  organizeInvestigation: vi.fn(),
}));
vi.mock("../../api/managedResearch", () => ({ getManagedResearchJobs: vi.fn() }));

describe("CompanyInvestigationsTab", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getManagedResearchJobs).mockResolvedValue([]);
    vi.mocked(getInvestigationOrganization).mockRejectedValue(new Error("not organized yet"));
  });

  it("presents an investigation-first workspace and preserves raw material", async () => {
    vi.mocked(getSavedInvestigations).mockResolvedValue([{
      id: "artifact-1",
      companyId: "company-1",
      title: "Japan expansion",
      question: "How has the company expanded in Japan?",
      objective: "How has the company expanded in Japan?",
      summary: "The company announced a Japan partnership.",
      result: "Original imported response",
      rawResponse: "## Research Summary\nOriginal imported response",
      createdAt: "2026-09-18T10:00:00Z",
      researchType: "Deep",
      sourceCount: 1,
      sourceDocumentIds: [],
      origin: "ExternalImport",
      sourceLeads: [{ id: "lead-1", url: "https://example.com/japan", title: "Japan update" }],
      claims: [{ field: "Markets", statement: "The company entered Japan.", supportingSourceLeadIds: ["lead-1"] }],
      uncertainties: ["Date needs confirmation."],
    }]);

    render(<MemoryRouter><CompanyInvestigationsTab companyId="company-1" companyName="Northwind" /></MemoryRouter>);

    expect(await screen.findByRole("heading", { name: "Investigations" })).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Current investigations" })).toHaveValue("artifact-1");
    expect(await screen.findByRole("heading", { name: "Executive summary" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Markets" })).toBeInTheDocument();
    expect(screen.getByText("Source leads available")).toBeInTheDocument();
    expect(screen.getByText("External AI Assist")).toBeInTheDocument();
    expect(screen.getByText("Preserved original material · not accepted evidence")).toBeInTheDocument();

    await userEvent.setup().click(screen.getByText("Raw research material / activity"));
    expect(screen.getByText(/Original imported response/)).toBeInTheDocument();
  });

  it("organizes existing material without replacing the raw investigation", async () => {
    const user = userEvent.setup();
    vi.mocked(getSavedInvestigations).mockResolvedValue([{
      id: "artifact-1",
      companyId: "company-1",
      title: "Leadership verification",
      question: "Who leads the company?",
      summary: "A leadership note.",
      result: "Raw leadership response",
      createdAt: "2026-09-18T10:00:00Z",
      researchType: "Deep",
      sourceCount: 0,
      sourceDocumentIds: [],
      origin: "ExternalImport",
      claims: [{ field: "Leadership", statement: "Jane Doe is CEO." }],
      sourceLeads: [],
      uncertainties: [],
    }]);
    vi.mocked(organizeInvestigation).mockResolvedValue({
      id: "organization-1",
      savedResearchArtifactId: "artifact-1",
      version: 1,
      createdAt: "2026-09-18T10:02:00Z",
      executiveSummary: "Organized leadership findings.",
      themes: [{ name: "Leadership", summary: "Current executive leadership.", claimCount: 1 }],
      evidenceGaps: ["No source lead was attached."],
      suggestedFollowUps: ["Verify the current CEO on an official source."],
      uncertainties: [],
      isHumanEdited: false,
    });

    render(<MemoryRouter><CompanyInvestigationsTab companyId="company-1" companyName="Northwind" /></MemoryRouter>);
    await screen.findByRole("heading", { name: "Leadership verification" });
    await user.click(screen.getByRole("button", { name: /Organize investigations/ }));

    await waitFor(() => expect(screen.getByText("Organized leadership findings.")).toBeInTheDocument());
    await user.click(screen.getByText("Raw research material / activity"));
    expect(screen.getByText("Raw leadership response")).toBeInTheDocument();
    expect(organizeInvestigation).toHaveBeenCalledWith("company-1", "artifact-1");
  });
});
