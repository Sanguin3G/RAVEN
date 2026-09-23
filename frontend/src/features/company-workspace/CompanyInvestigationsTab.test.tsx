import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { getInvestigations, getInvestigationOrganization, markInvestigationDone, organizeInvestigation, reopenInvestigation, type Investigation } from "../../api/investigations";
import { getBriefings } from "../../api/briefings";
import { CompanyInvestigationsTab } from "./CompanyInvestigationsTab";

vi.mock("../../api/investigations", () => ({
  getInvestigations: vi.fn(), getInvestigationOrganization: vi.fn(), organizeInvestigation: vi.fn(),
  markInvestigationDone: vi.fn(), reopenInvestigation: vi.fn(),
}));
vi.mock("../../api/briefings", () => ({ getBriefings: vi.fn() }));

const general: Investigation = {
  id: "artifact-1", companyId: "company-1", materialKind: "Saved", materialId: "artifact-1",
  title: "Japan expansion", objective: "How has the company expanded in Japan?", summary: "A Japan partnership.",
  origin: "External AI Assist", purpose: "GeneralResearch", topics: ["Markets"], status: "Ready",
  materialUpdatedAt: "2026-09-18T10:00:00Z", profileImprovementLocked: false,
  claims: [{ field: "Markets", statement: "The company entered Japan." }],
  sourceLeads: [{ id: "lead-1", url: "https://example.com/japan", title: "Japan update" }],
  uncertainties: ["Date needs confirmation."], rawResponse: "Original imported response", briefingIds: [],
};
const improvement: Investigation = { ...general, id: "artifact-2", materialId: "artifact-2", title: "Strengthen legal identity", purpose: "ProfileImprovement", topics: ["Legal Identity"], origin: "RAVEN Research" };

describe("CompanyInvestigationsTab", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getInvestigations).mockResolvedValue([general, improvement]);
    vi.mocked(getBriefings).mockResolvedValue([]);
    vi.mocked(getInvestigationOrganization).mockRejectedValue(new Error("No analysis"));
  });

  it("switches through a searchable status list and keeps purpose separate from topic", async () => {
    const user = userEvent.setup();
    const onImproveProfile = vi.fn();
    render(<MemoryRouter><CompanyInvestigationsTab companyId="company-1" companyName="Northwind" onImproveProfile={onImproveProfile} /></MemoryRouter>);
    expect(await screen.findByRole("heading", { name: "Japan expansion" })).toBeInTheDocument();
    expect(screen.getByText("General research")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Improve profile" })).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Japan expansion.*External AI Assist/ }));
    await user.type(screen.getByRole("searchbox", { name: "Search investigations" }), "legal identity");
    await user.click(screen.getByRole("button", { name: /Strengthen legal identity.*RAVEN Research/ }));
    expect(screen.getByRole("heading", { name: "Strengthen legal identity" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Improve profile" }));
    expect(onImproveProfile).toHaveBeenCalledWith(["LegalIdentity"], "artifact-2", "saved");
  });

  it("marks material done and reopens it through the server", async () => {
    const user = userEvent.setup();
    vi.mocked(markInvestigationDone).mockResolvedValue({ ...general, status: "Done" });
    vi.mocked(reopenInvestigation).mockResolvedValue(general);
    vi.mocked(getInvestigations).mockResolvedValueOnce([general]).mockResolvedValueOnce([{ ...general, status: "Done" }]).mockResolvedValueOnce([general]);
    render(<MemoryRouter><CompanyInvestigationsTab companyId="company-1" /></MemoryRouter>);
    await screen.findByRole("heading", { name: "Japan expansion" });
    await user.click(screen.getByRole("button", { name: "Mark as done" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Reopen" })).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: "Reopen" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "Mark as done" })).toBeInTheDocument());
    expect(markInvestigationDone).toHaveBeenCalledWith("company-1", "artifact-1");
    expect(reopenInvestigation).toHaveBeenCalledWith("company-1", "artifact-1");
  });

  it("analyzes only the selected saved Investigation and preserves raw material", async () => {
    const user = userEvent.setup();
    vi.mocked(getInvestigations).mockResolvedValue([general]);
    vi.mocked(organizeInvestigation).mockResolvedValue({
      id: "analysis-1", savedResearchArtifactId: "artifact-1", version: 1, createdAt: "2026-09-18T10:02:00Z",
      executiveSummary: "The partnership is a market signal.", themes: [{ name: "Markets", summary: "Japan activity", claimCount: 1 }],
      evidenceGaps: ["Date unclear"], suggestedFollowUps: ["Verify announcement date"], uncertainties: [], isHumanEdited: false,
    });
    render(<MemoryRouter><CompanyInvestigationsTab companyId="company-1" /></MemoryRouter>);
    await screen.findByRole("heading", { name: "Japan expansion" });
    await user.click(screen.getByRole("button", { name: "Analyze findings" }));
    expect(await screen.findByText("The partnership is a market signal.")).toBeInTheDocument();
    expect(organizeInvestigation).toHaveBeenCalledWith("company-1", "artifact-1");
    await user.click(screen.getByText("Raw research material / activity"));
    expect(screen.getByText("Original imported response")).toBeInTheDocument();
  });
});
