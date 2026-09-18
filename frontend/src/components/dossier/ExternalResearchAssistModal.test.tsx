import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  generateExternalResearchBrief,
  getExternalResearchAnalysis,
  importExternalResearch,
  startExternalResearchAnalysis,
} from "../../api/externalResearch";
import { ExternalResearchAssistModal } from "./ExternalResearchAssistModal";
import { dismissResearchActivity, useResearchActivities } from "../../utils/researchActivity";

vi.mock("../../api/externalResearch", () => ({
  generateExternalResearchBrief: vi.fn(),
  getExternalResearchAnalysis: vi.fn(),
  importExternalResearch: vi.fn(),
  startExternalResearchAnalysis: vi.fn(),
}));

const company = { id: "company-1", displayName: "Northwind Research", country: "Vietnam" };

function ActivityCount() {
  return <output data-testid="research-activity-count">{useResearchActivities().length}</output>;
}

describe("ExternalResearchAssistModal", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
    dismissResearchActivity("external-research-company-1-Leadership");
    vi.mocked(generateExternalResearchBrief).mockResolvedValue({ objective: "Current leadership", focusedTargets: ["Leadership"], markdown: "Research Northwind leadership.\nInclude exact URLs." });
    vi.mocked(startExternalResearchAnalysis).mockResolvedValue({ id: "analysis-1", companyId: "company-1", question: "Current leadership", status: "Queued", createdAt: "2026-09-18T10:00:00Z" });
    vi.mocked(getExternalResearchAnalysis).mockResolvedValue({
      id: "analysis-1",
      companyId: "company-1",
      question: "Current leadership",
      status: "Completed",
      createdAt: "2026-09-18T10:00:00Z",
      completedAt: "2026-09-18T10:00:02Z",
      result: {
        summary: "Leadership was identified from two source leads.",
        claims: [{ field: "Leadership", statement: "Jane Doe is regional CEO.", supportingSourceLeadIds: ["lead-1"] }],
        sourceLeads: [{ id: "lead-1", url: "https://northwind.example/leadership", title: "Leadership", publisher: "Northwind" }],
        uncertainties: ["The appointment date needs confirmation."],
        suggestedFollowUps: [],
        rawMarkdown: "## Claims",
      },
    });
    vi.mocked(importExternalResearch).mockResolvedValue({} as never);
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText: vi.fn().mockResolvedValue(undefined) } });
  });

  it("copies a focused brief, analyzes asynchronously, reviews, and saves unverified material without re-crawling links", async () => {
    render(<ExternalResearchAssistModal company={company} target="Leadership" open onClose={vi.fn()} />);

    expect(await screen.findByRole("heading", { name: "External AI Assist — Leadership" })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByDisplayValue(/Research Northwind leadership/)).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "Copy prompt" }));
    expect(await screen.findByRole("button", { name: "Copied" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /I've got a response/ }));
    fireEvent.change(screen.getByLabelText("Assistant response"), { target: { value: "## Claims\n- Field: Leadership\n  Claim: Jane Doe is regional CEO." } });
    fireEvent.click(screen.getByRole("button", { name: "Analyze" }));

    expect(startExternalResearchAnalysis).toHaveBeenCalledWith("company-1", expect.objectContaining({ question: "Current leadership" }));
    expect(await screen.findByRole("heading", { name: "Review imported research" })).toBeInTheDocument();
    expect(screen.getByText("Unverified research material")).toBeInTheDocument();
    expect(screen.getByText("Jane Doe is regional CEO.")).toBeInTheDocument();

    expect(screen.queryByRole("button", { name: /Verify selected sources/ })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Save to Investigation" }));
    await waitFor(() => expect(importExternalResearch).toHaveBeenCalledWith("company-1", expect.objectContaining({ markdown: expect.any(String), question: "Current leadership" })));
  });

  it("keeps the activity strip entry when the user closes the modal so durable work can be reopened", async () => {
    const onClose = vi.fn();
    render(<><ExternalResearchAssistModal company={company} target="Leadership" open onClose={onClose} /><ActivityCount /></>);

    await waitFor(() => expect(screen.getByTestId("research-activity-count")).toHaveTextContent("1"));
    fireEvent.click(screen.getByRole("button", { name: "Close External AI Assist" }));

    expect(onClose).toHaveBeenCalledOnce();
    expect(screen.getByTestId("research-activity-count")).toHaveTextContent("1");
  });
});
