import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createChatConversation, sendChatMessage } from "../../api/chat";
import { AskRavenHandoff } from "./AskRavenHandoff";

vi.mock("../../api/chat", () => ({
  createChatConversation: vi.fn(),
  sendChatMessage: vi.fn(),
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
  beforeEach(() => vi.clearAllMocks());

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
});
