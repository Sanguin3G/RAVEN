import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createChatConversation, getChatConversation, sendChatMessageStream, updateChatCapabilities } from "../../api/chat";
import { AskRavenHandoff } from "./AskRavenHandoff";

vi.mock("../../api/chat", () => ({
  createChatConversation: vi.fn(),
  getChatConversation: vi.fn(),
  sendChatMessageStream: vi.fn(),
  updateChatCapabilities: vi.fn(),
}));

const props = {
  companyId: "company-1",
  companyName: "FPT Smart Cloud",
  profileVersion: 2,
  profileVersionId: "profile-2",
  sourceCount: 17,
  lastResearchedAt: "2026-09-11T09:00:00Z",
};

function renderHandoff(path = "/companies/company-1") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AskRavenHandoff {...props} />
    </MemoryRouter>,
  );
}

describe("AskRavenHandoff", () => {
  beforeEach(() => vi.clearAllMocks());

  it("renders the profile-only boundary", () => {
    renderHandoff();

    expect(screen.getByRole("heading", { name: "Ask RAVEN" })).toBeInTheDocument();
    expect(screen.getByText(/FPT Smart Cloud.*v2.*17 sources/)).toBeInTheDocument();
    expect(screen.getByText("Profile only")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Additional capabilities" })).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByText("What does this company do?")).toBeInTheDocument();
  });

  it("persists the web search permission on the conversation", async () => {
    vi.mocked(createChatConversation).mockResolvedValue({ id: "conversation-1", webSearchEnabled: false, messages: [] } as never);
    vi.mocked(updateChatCapabilities).mockResolvedValue({ id: "conversation-1", webSearchEnabled: true, messages: [] } as never);
    vi.mocked(getChatConversation).mockResolvedValue({ id: "conversation-1", webSearchEnabled: true, messages: [] } as never);
    renderHandoff();

    fireEvent.click(screen.getByRole("button", { name: "Additional capabilities" }));
    expect(screen.getByRole("link", { name: /Open Investigations/ })).toHaveAttribute("href", "/companies/company-1?tab=investigations");
    const webSearch = screen.getByRole("checkbox", { name: "Web search" });
    expect(webSearch).not.toBeChecked();

    fireEvent.click(webSearch);

    await waitFor(() => expect(updateChatCapabilities).toHaveBeenCalledWith("company-1", "conversation-1", { webSearchEnabled: true }));
    expect(screen.getByRole("checkbox", { name: "Web search" })).toBeChecked();
  });

  it("restores the conversation and web search permission from the URL", async () => {
    vi.mocked(getChatConversation).mockResolvedValue({
      id: "conversation-restore",
      webSearchEnabled: true,
      messages: [{
        id: "assistant-1",
        role: "Assistant",
        content: "Restored answer.",
        status: "Completed",
        answerStatus: "Answered",
        citations: [{ origin: "Web", webEvidenceSnapshotId: "web-1", title: "Company update", url: "https://example.com/update", retrievedAt: "2026-09-11T09:00:00Z" }],
        webEvidenceSnapshots: [{
          id: "web-1",
          url: "https://example.com/update",
          title: "Company update",
          contentExcerpt: "Update evidence.",
          searchProvider: "brave",
          crawlerProvider: "crawl4ai-local",
          searchRank: 1,
          retrievedAt: "2026-09-11T09:00:00Z",
        }],
        toolExecutions: [],
        createdAt: "2026-09-11T09:00:00Z",
      }],
    } as never);
    renderHandoff("/companies/company-1?conversation=conversation-restore");

    await waitFor(() => expect(getChatConversation).toHaveBeenCalledWith("company-1", "conversation-restore"));
    expect(screen.getByText("Restored answer.")).toBeInTheDocument();
    expect(screen.getByText("Web permitted")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Company update/ })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Nguồn" }));
    expect(screen.getByRole("link", { name: /Company update/ })).toHaveAttribute("href", "https://example.com/update");
    expect(screen.getByText("Web", { exact: true })).toBeInTheDocument();
    expect(screen.queryByText(/brave.*result #1/)).not.toBeInTheDocument();
  });

  it("starts a new local conversation without deleting the restored conversation", async () => {
    vi.mocked(getChatConversation).mockResolvedValue({
      id: "conversation-restore",
      webSearchEnabled: true,
      messages: [{
        id: "assistant-1",
        role: "Assistant",
        content: "Earlier answer.",
        status: "Completed",
        citations: [],
        webEvidenceSnapshots: [],
        toolExecutions: [],
        createdAt: "2026-09-11T09:00:00Z",
      }],
    } as never);
    renderHandoff("/companies/company-1?conversation=conversation-restore");

    await waitFor(() => expect(screen.getByText("Earlier answer.")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "New conversation" }));

    expect(screen.queryByText("Earlier answer.")).not.toBeInTheDocument();
    expect(screen.getByText("Ask about this company")).toBeInTheDocument();
    expect(createChatConversation).not.toHaveBeenCalled();
    expect(screen.getByText("Profile only")).toBeInTheDocument();
  });

  it("requires an accepted profile before enabling the composer", () => {
    render(<MemoryRouter><AskRavenHandoff {...props} profileVersion={null} profileVersionId={null} /></MemoryRouter>);

    expect(screen.getByText("Profile required")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Accept a profile to ask questions…")).toBeDisabled();
  });

  it("creates a conversation and renders the structured answer", async () => {
    vi.mocked(createChatConversation).mockResolvedValue({ id: "conversation-1", webSearchEnabled: false } as never);
    vi.mocked(sendChatMessageStream).mockImplementation(async (_companyId, _conversationId, _payload, onEvent) => {
      onEvent({ type: "progress", stage: "CheckingProfile", message: "Checking the accepted profile" });
      onEvent({ type: "completed", response: { messageId: "message-1", answer: "FPT Smart Cloud operates in cloud services.", status: "Answered", citations: [{ origin: "Profile", sourceDocumentId: "source-1", title: "Profile", url: "https://example.com/profile", retrievedAt: "2026-09-11T09:00:00Z" }, { origin: "Web", webEvidenceSnapshotId: "web-1", title: "Company update", url: "https://example.com/update", retrievedAt: "2026-09-11T09:00:00Z" }], webEvidenceSnapshots: [], toolExecutions: [] } as never });
    });
    renderHandoff();

    fireEvent.change(screen.getByLabelText("Ask about FPT Smart Cloud"), { target: { value: "What does it do?" } });
    fireEvent.click(screen.getByRole("button", { name: "Send question" }));

    await waitFor(() => expect(screen.getByText("FPT Smart Cloud operates in cloud services.")).toBeInTheDocument());
    expect(createChatConversation).toHaveBeenCalledWith("company-1");
    expect(sendChatMessageStream).toHaveBeenCalledWith("company-1", "conversation-1", { question: "What does it do?" }, expect.any(Function));
    expect(screen.getByText("Mixed", { exact: true })).toBeInTheDocument();
  });

  it("does not restore an empty conversation until the first streamed turn has completed", async () => {
    vi.mocked(createChatConversation).mockResolvedValue({ id: "conversation-1", webSearchEnabled: false } as never);
    vi.mocked(getChatConversation).mockResolvedValue({
      id: "conversation-1",
      webSearchEnabled: false,
      messages: [
        { id: "user-1", role: "User", content: "First question", status: "Completed", citations: [], webEvidenceSnapshots: [], toolExecutions: [], createdAt: "2026-09-18T00:00:00Z" },
        { id: "message-1", role: "Assistant", content: "Saved answer.", status: "Completed", answerStatus: "Answered", citations: [], webEvidenceSnapshots: [], toolExecutions: [], createdAt: "2026-09-18T00:00:01Z" },
      ],
    } as never);
    let completeStream: (() => void) | undefined;
    vi.mocked(sendChatMessageStream).mockImplementation((_companyId, _conversationId, _payload, onEvent) => new Promise<void>((resolve) => {
      completeStream = () => {
        onEvent({ type: "completed", response: { messageId: "message-1", answer: "Saved answer.", status: "Answered", citations: [], webEvidenceSnapshots: [], toolExecutions: [] } as never });
        resolve();
      };
    }));
    renderHandoff();

    fireEvent.change(screen.getByLabelText("Ask about FPT Smart Cloud"), { target: { value: "First question" } });
    fireEvent.click(screen.getByRole("button", { name: "Send question" }));
    await waitFor(() => expect(sendChatMessageStream).toHaveBeenCalled());
    expect(getChatConversation).not.toHaveBeenCalled();

    await act(async () => { completeStream?.(); });
    await waitFor(() => expect(screen.getByText("Saved answer.")).toBeInTheDocument());
  });
});
