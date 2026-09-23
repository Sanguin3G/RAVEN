import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createBriefing, getBriefing, getBriefings, getBriefingChanges, getBriefingVersions, getNewerBriefingInvestigations, updateBriefing, type Briefing } from "../../api/briefings";
import { getInvestigations, type Investigation } from "../../api/investigations";
import { CompanyBriefingsTab } from "./CompanyBriefingsTab";

vi.mock("../../api/briefings", async importOriginal => ({
  ...await importOriginal<typeof import("../../api/briefings")>(),
  createBriefing: vi.fn(), getBriefing: vi.fn(), getBriefings: vi.fn(), getBriefingChanges: vi.fn(),
  getBriefingVersions: vi.fn(), getNewerBriefingInvestigations: vi.fn(), updateBriefing: vi.fn(),
}));
vi.mock("../../api/investigations", () => ({ getInvestigations: vi.fn() }));

const material: Investigation = {
  id: "investigation-1", companyId: "company-1", materialKind: "Saved", materialId: "investigation-1",
  title: "Recruitment activity", objective: "Hiring trends", summary: "Engineering roles", origin: "Deep Research",
  purpose: "GeneralResearch", topics: ["Talent & Hiring"], status: "Ready", materialUpdatedAt: "2026-09-18T00:00:00Z",
  profileImprovementLocked: false, claims: [], sourceLeads: [], uncertainties: [], briefingIds: [],
};
const second: Investigation = { ...material, id: "investigation-2", materialId: "investigation-2", title: "European hiring", materialUpdatedAt: "2026-09-20T00:00:00Z" };
const briefing: Briefing = {
  id: "briefing-1", companyId: "company-1", title: "Talent & Hiring", template: "Talent & Hiring", objective: "Hiring trends",
  createdAt: "2026-09-19T00:00:00Z", updatedAt: "2026-09-19T00:00:00Z", versionCount: 1, newerRelevantCount: 1,
  currentVersion: { id: "version-1", versionNumber: 1, generatedAt: "2026-09-19T00:00:00Z", researchThrough: material.materialUpdatedAt,
    title: "Talent & Hiring", template: "Talent & Hiring", objective: "Hiring trends",
    sections: [{ key: "Key takeaways", title: "Key takeaways", items: ["Engineering recruitment"], sourceInvestigationIds: [material.id] }],
    sources: [{ investigationId: material.id, materialKind: "Saved", materialId: material.id, title: material.title, origin: material.origin,
      purpose: material.purpose, topics: material.topics, materialUpdatedAt: material.materialUpdatedAt, summary: material.summary,
      claims: [], sourceLeads: [], uncertainties: [] }],
  },
};

describe("CompanyBriefingsTab", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    Object.defineProperty(HTMLDialogElement.prototype, "showModal", { configurable: true, value() { this.setAttribute("open", ""); } });
    Object.defineProperty(HTMLDialogElement.prototype, "close", { configurable: true, value() { this.removeAttribute("open"); } });
    vi.mocked(getInvestigations).mockResolvedValue([material, second]);
    vi.mocked(getBriefings).mockResolvedValue([]);
    vi.mocked(getBriefing).mockResolvedValue(briefing);
    vi.mocked(getBriefingChanges).mockResolvedValue({ fromVersion: 1, toVersion: 2, newMaterial: ["European hiring"], changedMaterial: [], removedMaterial: [], newUncertainties: [] });
    vi.mocked(getBriefingVersions).mockResolvedValue([briefing.currentVersion]);
    vi.mocked(getNewerBriefingInvestigations).mockResolvedValue([{ id: second.id, title: second.title, origin: second.origin, purpose: second.purpose, topics: second.topics, materialUpdatedAt: second.materialUpdatedAt }]);
  });

  it("creates a General Research Briefing from a user-selected Investigation without a Profile", async () => {
    const user = userEvent.setup();
    vi.mocked(createBriefing).mockResolvedValue(briefing);
    render(<CompanyBriefingsTab companyId="company-1" />);
    await screen.findByRole("heading", { name: /Turn selected research into reusable company intelligence/ });
    await user.click(screen.getByRole("button", { name: "Create your first Briefing" }));
    const dialog = screen.getByRole("dialog", { name: "Create briefing" });
    await user.type(within(dialog).getByRole("textbox", { name: "Title" }), "Talent & Hiring");
    await user.type(within(dialog).getByRole("textbox", { name: /Objective/ }), "Hiring trends");
    await user.click(within(dialog).getByRole("checkbox", { name: /Recruitment activity/ }));
    await user.click(within(dialog).getByRole("button", { name: "Create briefing" }));
    await waitFor(() => expect(createBriefing).toHaveBeenCalledWith("company-1", {
      title: "Talent & Hiring", template: "Talent & Hiring", objective: "Hiring trends", investigationIds: [material.id],
    }));
  });

  it("updates from selected newer research and keeps version history accessible", async () => {
    const user = userEvent.setup();
    vi.mocked(getBriefings).mockResolvedValue([{ id: briefing.id, title: briefing.title, template: briefing.template,
      generatedAt: briefing.currentVersion.generatedAt, researchThrough: briefing.currentVersion.researchThrough,
      versionNumber: 1, sourceCount: 1, newerRelevantCount: 1 }]);
    const updated = { ...briefing, versionCount: 2, newerRelevantCount: 0, currentVersion: { ...briefing.currentVersion,
      id: "version-2", versionNumber: 2, sources: [...briefing.currentVersion.sources, { ...briefing.currentVersion.sources[0], investigationId: second.id, title: second.title, materialUpdatedAt: second.materialUpdatedAt }] } };
    vi.mocked(updateBriefing).mockResolvedValue(updated);
    vi.mocked(getBriefingVersions).mockResolvedValue([updated.currentVersion, briefing.currentVersion]);
    render(<CompanyBriefingsTab companyId="company-1" />);
    await screen.findByRole("heading", { name: "Talent & Hiring" });
    expect(screen.getAllByText(/New research available/).length).toBeGreaterThan(0);
    await user.click(screen.getByRole("button", { name: /Update briefing/ }));
    const dialog = screen.getByRole("dialog", { name: "Update briefing" });
    await user.click(await within(dialog).findByRole("checkbox", { name: /European hiring/ }));
    await user.click(within(dialog).getByRole("button", { name: "Update briefing" }));
    await waitFor(() => expect(updateBriefing).toHaveBeenCalledWith("company-1", briefing.id, { newInvestigationIds: [second.id] }));
    await user.click(screen.getByText(/More/));
    await user.click(screen.getByRole("button", { name: "Version history" }));
    const history = await screen.findByRole("dialog", { name: "Version history" });
    expect(within(history).getByRole("button", { name: /v1/ })).toBeInTheDocument();
  });

  it("disables an Add to briefing target that already contains the Investigation", async () => {
    vi.mocked(getBriefings).mockResolvedValue([{ id: briefing.id, title: briefing.title, template: briefing.template,
      generatedAt: briefing.currentVersion.generatedAt, researchThrough: briefing.currentVersion.researchThrough,
      versionNumber: 1, sourceCount: 1, newerRelevantCount: 0 }]);
    render(<CompanyBriefingsTab companyId="company-1" initialInvestigationId={material.id} />);

    const existingTarget = await screen.findByRole("button", { name: /Talent & Hiring.*Already included/ });
    expect(existingTarget).toBeDisabled();
    expect(updateBriefing).not.toHaveBeenCalled();
  });
});
