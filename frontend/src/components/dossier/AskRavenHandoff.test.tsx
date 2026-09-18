import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createChatConversation, sendChatMessage } from "../../api/chat";
import {
  attachResearchContext,
  getManagedResearchJobs,
  getResearchContextAttachments,
  removeResearchContext,
  startManagedResearch,
} from "../../api/managedResearch";
import { AskRavenHandoff } from "./AskRavenHandoff";

vi.mock("../../api/chat", () => ({
  createChatConversation: vi.fn(),
  sendChatMessage: vi.fn(),
}));

vi.mock("../../api/managedResearch", () => ({
  attachResearchContext: vi.fn(),
  getManagedResearchJobs: vi.fn(),
  getResearchContextAttachments: vi.fn(),
  removeResearchContext: vi.fn(),
  startManagedResearch: vi.fn(),
}));

const props = {
  companyId: "company-1",
  companyName: "FPT Smart Cloud",
  profileVersion: 2,
  profileVersionId: "profile-2",
  sourceCount: 17,
  lastResearchedAt: "2026-09-11T09:00:00Z",
};

describe("AskRavenHandoff", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getManagedResearchJobs).mockResolvedValue([]);
    vi.mocked(getResearchContextAttachments).mockResolvedValue([]);
  });

  it("renders the profile-only boundary", () => {
    render(<AskRavenHandoff {...props} />);

    expect(screen.getByRole("heading", { name: "Ask RAVEN" })).toBeInTheDocument();
    expect(screen.getByText(/FPT Smart Cloud.*v2.*17 sources/)).toBeInTheDocument();
    expect(screen.getByText("Profile only")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Additional capabilities" })).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByText("What does this company do?")).toBeInTheDocument();
  });

  it("keeps web search disabled and exposes only a real investigations route", () => {
    render(<AskRavenHandoff {...props} />);

    fireEvent.click(screen.getByRole("button", { name: "Additional capabilities" }));
    expect(screen.getByRole("link", { name: /Open Investigations/ })).toHaveAttribute("href", "/companies/company-1?tab=investigations");
    expect(screen.getByRole("button", { name: /Search the web/ })).toBeDisabled();
  });

  it("requires an accepted profile before enabling the composer", () => {
    render(<AskRavenHandoff {...props} profileVersion={null} profileVersionId={null} />);

    expect(screen.getByText("Profile required")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Accept a profile to ask questions…")).toBeDisabled();
  });

  it("creates a conversation and renders the structured answer", async () => {
    vi.mocked(createChatConversation).mockResolvedValue({ id: "conversation-1" } as never);
    vi.mocked(sendChatMessage).mockResolvedValue({
      messageId: "message-1",
      answer: "FPT Smart Cloud operates in cloud services.",
      status: "Answered",
      citations: [],
      toolExecutions: [],
    } as never);
    render(<AskRavenHandoff {...props} />);

    fireEvent.change(screen.getByLabelText("Ask about FPT Smart Cloud"), { target: { value: "What does it do?" } });
    fireEvent.click(screen.getByRole("button", { name: "Send question" }));

    await waitFor(() => expect(screen.getByText("FPT Smart Cloud operates in cloud services.")).toBeInTheDocument());
    expect(createChatConversation).toHaveBeenCalledWith("company-1");
    expect(sendChatMessage).toHaveBeenCalledWith("company-1", "conversation-1", { question: "What does it do?" });
  });

  it("selects Deep Research as a distinct composer capability", async () => {
    render(<AskRavenHandoff {...props} />);

    fireEvent.click(screen.getByRole("button", { name: "Additional capabilities" }));
    const deepResearch = screen.getByRole("button", { name: /Deep Research/ });
    expect(deepResearch).not.toBeDisabled();
    fireEvent.click(deepResearch);

    const capabilityTrigger = screen.getByRole("button", { name: "Additional capabilities" });
    const capabilityChip = screen.getByRole("button", { name: "Remove Deep Research capability" });
    expect(capabilityChip).toBeInTheDocument();
    expect(capabilityTrigger.compareDocumentPosition(capabilityChip) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    await waitFor(() => expect(screen.getByPlaceholderText(/investigate about FPT Smart Cloud/i)).toBeInTheDocument());
  });

  it("starts managed research without sending a normal Chat turn", async () => {
    vi.mocked(createChatConversation).mockResolvedValue({ id: "conversation-1" } as never);
    vi.mocked(startManagedResearch).mockResolvedValue({
      id: "job-1",
      companyId: "company-1",
      objective: "FPT Smart Cloud expansion in Japan",
      provider: "exa-agent",
      status: "Queued",
      createdAt: "2026-09-18T09:00:00Z",
    });
    render(<AskRavenHandoff {...props} />);

    fireEvent.click(screen.getByRole("button", { name: "Additional capabilities" }));
    fireEvent.click(screen.getByRole("button", { name: /Deep Research/ }));
    fireEvent.change(await screen.findByPlaceholderText(/investigate about FPT Smart Cloud/i), { target: { value: "FPT Smart Cloud expansion in Japan" } });
    fireEvent.click(screen.getByRole("button", { name: "Start Deep Research" }));

    await waitFor(() => expect(screen.getByText(/Deep Research started/)).toBeInTheDocument());
    expect(startManagedResearch).toHaveBeenCalledWith("company-1", "FPT Smart Cloud expansion in Japan", { conversationId: "conversation-1" });
    expect(createChatConversation).toHaveBeenCalledWith("company-1");
    expect(sendChatMessage).not.toHaveBeenCalled();
    expect(screen.getByRole("link", { name: "View investigation" })).toHaveAttribute("href", "/companies/company-1?tab=investigations&research=job-1");
    expect(screen.getByRole("button", { name: "Send question" })).toBeInTheDocument();
  });

  it("keeps completed research out of the floating notification layer", async () => {
    vi.mocked(getManagedResearchJobs).mockResolvedValue([{
      id: "job-complete",
      companyId: "company-1",
      objective: "Recent expansion",
      provider: "exa-agent",
      status: "Completed",
      createdAt: new Date().toISOString(),
      completedAt: new Date().toISOString(),
      investigationId: "investigation-1",
    }]);
    render(<AskRavenHandoff {...props} />);

    await waitFor(() => expect(screen.getByRole("link", { name: "Open" })).toHaveAttribute("href", "/companies/company-1?tab=investigations&research=investigation-1"));
    expect(screen.queryByTestId("managed-research-completion")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Send question" })).toBeInTheDocument();
  });

  it("attaches and removes an investigation context without changing the Chat contract", async () => {
    vi.mocked(createChatConversation).mockResolvedValue({ id: "conversation-1" } as never);
    vi.mocked(getManagedResearchJobs).mockResolvedValue([{
      id: "job-complete",
      companyId: "company-1",
      objective: "Recent expansion",
      provider: "exa-agent",
      status: "Completed",
      createdAt: new Date().toISOString(),
      completedAt: new Date().toISOString(),
      investigationId: "investigation-1",
    }]);
    vi.mocked(attachResearchContext).mockResolvedValue({
      id: "attachment-1",
      companyId: "company-1",
      conversationId: "conversation-1",
      investigationId: "investigation-1",
      origin: "ManagedAi",
      objective: "Recent expansion",
      summary: "Research summary",
      completedAt: new Date().toISOString(),
      attachedAt: new Date().toISOString(),
    });
    render(<AskRavenHandoff {...props} />);

    await waitFor(() => expect(screen.getByRole("button", { name: "Attach to Ask RAVEN" })).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "Attach to Ask RAVEN" }));

    await waitFor(() => expect(screen.getByLabelText("Attached research context")).toBeInTheDocument());
    expect(attachResearchContext).toHaveBeenCalledWith("company-1", "investigation-1", "conversation-1");
    expect(screen.getByLabelText("Attached research context")).toHaveTextContent("Recent expansion");

    fireEvent.click(screen.getByRole("button", { name: "Remove Recent expansion context" }));
    await waitFor(() => expect(removeResearchContext).toHaveBeenCalledWith("company-1", "investigation-1", "conversation-1"));
    await waitFor(() => expect(screen.queryByLabelText("Attached research context")).not.toBeInTheDocument());
  });
});
