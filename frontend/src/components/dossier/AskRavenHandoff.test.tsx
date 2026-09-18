import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createChatConversation, sendChatMessage } from "../../api/chat";
import { getManagedResearchJobs, startManagedResearch } from "../../api/managedResearch";
import { AskRavenHandoff } from "./AskRavenHandoff";

vi.mock("../../api/chat", () => ({
  createChatConversation: vi.fn(),
  sendChatMessage: vi.fn(),
}));

vi.mock("../../api/managedResearch", () => ({
  getManagedResearchJobs: vi.fn(),
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

    expect(screen.getByText("Launches a background investigation; you can keep chatting.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Remove Deep Research capability" })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByPlaceholderText(/investigate about FPT Smart Cloud/i)).toBeInTheDocument());
  });

  it("starts managed research without sending a normal Chat turn", async () => {
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
    expect(startManagedResearch).toHaveBeenCalledWith("company-1", "FPT Smart Cloud expansion in Japan");
    expect(createChatConversation).not.toHaveBeenCalled();
    expect(sendChatMessage).not.toHaveBeenCalled();
    expect(screen.getByRole("link", { name: "View investigation" })).toHaveAttribute("href", "/companies/company-1?tab=investigations&research=job-1");
    expect(screen.getByRole("button", { name: "Send question" })).toBeInTheDocument();
  });

  it("surfaces a recent completed investigation without blocking the composer", async () => {
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

    await waitFor(() => expect(screen.getByTestId("managed-research-completion")).toBeInTheDocument());
    expect(screen.getByTestId("managed-research-completion").querySelector("a")).toHaveAttribute("href", "/companies/company-1?tab=investigations&research=investigation-1");
  });
});
