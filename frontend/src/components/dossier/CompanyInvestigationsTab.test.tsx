import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { getSavedInvestigations } from "../../api/investigations";
import { getManagedResearchJobs } from "../../api/managedResearch";
import { generateExternalResearchBrief, importExternalResearch, previewExternalResearchImport } from "../../api/externalResearch";
import { CompanyInvestigationsTab } from "./CompanyInvestigationsTab";

vi.mock("../../api/investigations", () => ({ getSavedInvestigations: vi.fn() }));
vi.mock("../../api/managedResearch", () => ({ getManagedResearchJobs: vi.fn() }));
vi.mock("../../api/externalResearch", () => ({
  generateExternalResearchBrief: vi.fn(),
  previewExternalResearchImport: vi.fn(),
  importExternalResearch: vi.fn(),
}));

describe("CompanyInvestigationsTab external research", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getSavedInvestigations).mockResolvedValue([]);
    vi.mocked(getManagedResearchJobs).mockResolvedValue([]);
  });

  it("prepares a brief, previews pasted claims and saves them as reviewable material", async () => {
    vi.mocked(generateExternalResearchBrief).mockResolvedValue({
      objective: "Recent expansion",
      focusedTargets: ["Markets"],
      markdown: "## Research Summary\nA focused note.",
    });
    vi.mocked(previewExternalResearchImport).mockResolvedValue({
      summary: "A pasted summary.",
      claims: [{ field: "Markets", statement: "The company entered Japan." }],
      sourceLeads: [{ id: "lead-1", url: "https://example.com/japan", title: "Japan update" }],
      uncertainties: ["Date needs confirmation."],
      suggestedFollowUps: [],
      rawMarkdown: "Research Summary\nA pasted summary.",
    });
    vi.mocked(importExternalResearch).mockResolvedValue({
      id: "artifact-1",
      companyId: "company-1",
      title: "External research: Japan",
      question: "What changed in Japan?",
      summary: "A pasted summary.",
      createdAt: "2026-09-18T10:00:00Z",
      researchType: "Deep",
      sourceCount: 0,
      sourceDocumentIds: [],
      origin: "ExternalImport",
      sourceLeads: [{ id: "lead-1", url: "https://example.com/japan", title: "Japan update" }],
      claims: [{ field: "Markets", statement: "The company entered Japan." }],
    });

    render(<CompanyInvestigationsTab companyId="company-1" />);
    fireEvent.change(screen.getByLabelText("Optional research objective"), { target: { value: "Recent expansion" } });
    fireEvent.click(screen.getByRole("button", { name: "Prepare and copy brief" }));
    await waitFor(() => expect(screen.getAllByDisplayValue(/Research Summary/).length).toBeGreaterThan(0));

    fireEvent.change(screen.getByLabelText("Research question"), { target: { value: "What changed in Japan?" } });
    fireEvent.change(screen.getByLabelText("Paste external response"), { target: { value: "## Research Summary\nA pasted summary." } });
    fireEvent.click(screen.getByRole("button", { name: "Preview notes" }));
    await waitFor(() => expect(screen.getByTestId("external-research-preview")).toHaveTextContent("The company entered Japan."));
    expect(screen.getByRole("link", { name: "Japan update" })).toHaveAttribute("href", "https://example.com/japan");

    fireEvent.click(screen.getByRole("button", { name: "Save to Investigations" }));
    await waitFor(() => expect(screen.getByText(/Saved as reviewable research notes/)).toBeInTheDocument());
    expect(importExternalResearch).toHaveBeenCalledWith("company-1", expect.objectContaining({ question: "What changed in Japan?" }));
    expect(screen.getByText("External research: Japan")).toBeInTheDocument();
  });
});
