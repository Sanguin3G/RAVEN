import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, useNavigate } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createChatConversation, getChatConversation, sendChatMessageStream, updateChatCapabilities } from "../../api/chat";
import { ApiError } from "../../api/client";
import {
  attachResearchContext,
  getManagedResearchJobs,
  getResearchContextAttachments,
  previewManagedResearchBrief,
  removeResearchContext,
  startManagedResearch,
} from "../../api/managedResearch";
import { AskRavenHandoff } from "./AskRavenHandoff";

vi.mock("../../api/chat", () => ({
  createChatConversation: vi.fn(),
  getChatConversation: vi.fn(),
  sendChatMessageStream: vi.fn(),
  updateChatCapabilities: vi.fn(),
}));

vi.mock("../../api/managedResearch", () => ({
  attachResearchContext: vi.fn(),
  getManagedResearchJobs: vi.fn(),
  getResearchContextAttachments: vi.fn(),
  previewManagedResearchBrief: vi.fn(),
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

const briefPreview = {
  question: "What public evidence describes FPT Smart Cloud's market presence, customers, and expansion in Japan?",
  contextRevision: "preview-revision",
};

function renderHandoff(path = "/companies/company-1") {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AskRavenHandoff {...props} />
    </MemoryRouter>,
  );
}

function InvestigationNavigation() {
  const navigate = useNavigate();
  return <button type="button" onClick={() => navigate("/companies/company-1?tab=investigations&research=job-1")}>Open running investigation</button>;
}

function renderHandoffWithInvestigationNavigation(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AskRavenHandoff {...props} />
      <InvestigationNavigation />
    </MemoryRouter>,
  );
}

describe("AskRavenHandoff", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getManagedResearchJobs).mockResolvedValue([]);
    vi.mocked(getResearchContextAttachments).mockResolvedValue([]);
    vi.mocked(previewManagedResearchBrief).mockResolvedValue(briefPreview as never);
  });

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

  it("keeps the current chat history when opening a running investigation", async () => {
    vi.mocked(getChatConversation).mockResolvedValue({
      id: "conversation-restore",
      webSearchEnabled: false,
      messages: [{
        id: "assistant-1",
        role: "Assistant",
        content: "Keep this answer visible.",
        status: "Completed",
        citations: [],
        webEvidenceSnapshots: [],
        toolExecutions: [],
        createdAt: "2026-09-23T09:00:00Z",
      }],
    } as never);
    renderHandoffWithInvestigationNavigation("/companies/company-1?conversation=conversation-restore");

    await waitFor(() => expect(screen.getByText("Keep this answer visible.")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "Open running investigation" }));

    expect(screen.getByText("Keep this answer visible.")).toBeInTheDocument();
    expect(getChatConversation).toHaveBeenCalledTimes(1);
  });

  it("requires an accepted profile before enabling the composer", () => {
    render(<MemoryRouter><AskRavenHandoff {...props} profileVersion={null} profileVersionId={null} /></MemoryRouter>);

    expect(screen.getByText("Profile required")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Accept a profile to ask questions…")).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Additional capabilities" }));
    expect(screen.getByRole("button", { name: /Deep Research/ })).toBeDisabled();
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

  it("selects Deep Research as a distinct composer capability", async () => {
    renderHandoff();

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

  it("requires confirmation of one editable research question before starting managed research", async () => {
    vi.mocked(createChatConversation).mockResolvedValue({ id: "conversation-1" } as never);
    vi.mocked(getChatConversation).mockResolvedValue({ id: "conversation-1", webSearchEnabled: false, messages: [{ id: "user-1", role: "User", content: "Which customers and expansion activities of FPT Smart Cloud in Japan are publicly documented?", status: "Completed", citations: [], webEvidenceSnapshots: [], toolExecutions: [], createdAt: "2026-09-18T09:00:00Z" }] } as never);
    vi.mocked(startManagedResearch).mockResolvedValue({
      id: "job-1",
      companyId: "company-1",
      objective: "Which customers and expansion activities of FPT Smart Cloud in Japan are publicly documented?",
      provider: "exa-agent",
      status: "Queued",
      answerInChat: true,
      conversationId: "conversation-1",
      chatMessageId: "user-1",
      createdAt: "2026-09-18T09:00:00Z",
    });
    renderHandoff();

    fireEvent.click(screen.getByRole("button", { name: "Additional capabilities" }));
    fireEvent.click(screen.getByRole("button", { name: /Deep Research/ }));
    fireEvent.change(await screen.findByPlaceholderText(/investigate about FPT Smart Cloud/i), { target: { value: "FPT Smart Cloud expansion in Japan" } });
    fireEvent.click(screen.getByRole("button", { name: "Review research question" }));

    await waitFor(() => expect(previewManagedResearchBrief).toHaveBeenCalledWith("company-1", "FPT Smart Cloud expansion in Japan"));
    expect(startManagedResearch).not.toHaveBeenCalled();
    expect(createChatConversation).not.toHaveBeenCalled();
    expect(screen.getByTestId("deep-research-brief")).toHaveTextContent(briefPreview.question);
    expect(screen.getByTestId("deep-research-brief")).toHaveTextContent("FPT Smart Cloud");
    expect(screen.getByTestId("deep-research-brief")).toHaveTextContent("v2");
    expect(screen.getByTestId("deep-research-brief")).toHaveTextContent("17 sources");
    expect(screen.getByTestId("deep-research-brief")).not.toHaveTextContent("FPT Smart Cloud expansion in Japan");
    fireEvent.click(screen.getByRole("button", { name: "Edit question" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Research question" }), { target: { value: "Which customers and expansion activities of FPT Smart Cloud in Japan are publicly documented?" } });
    fireEvent.click(screen.getByRole("button", { name: "Done editing" }));
    fireEvent.click(screen.getByRole("button", { name: "Start Deep Research" }));

    await waitFor(() => expect(screen.getByText(/Deep Research is running/)).toBeInTheDocument());
    expect(startManagedResearch).toHaveBeenCalledWith("company-1", "Which customers and expansion activities of FPT Smart Cloud in Japan are publicly documented?", {
      conversationId: "conversation-1",
      contextRevision: "preview-revision",
      answerInChat: true,
    });
    expect(createChatConversation).toHaveBeenCalledWith("company-1");
    expect(sendChatMessageStream).not.toHaveBeenCalled();
    expect(screen.getByText("Which customers and expansion activities of FPT Smart Cloud in Japan are publicly documented?")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Send question" })).toBeInTheDocument();
  });

  it("shows the provider outage and does not start research when question preview fails", async () => {
    vi.mocked(previewManagedResearchBrief).mockRejectedValue(new ApiError(503, {
      detail: "Gemini is temporarily unavailable. Retry the research question; Deep Research has not started.",
      code: "unavailable",
    }));
    renderHandoff();

    fireEvent.click(screen.getByRole("button", { name: "Additional capabilities" }));
    fireEvent.click(screen.getByRole("button", { name: /Deep Research/ }));
    fireEvent.change(await screen.findByPlaceholderText(/investigate about FPT Smart Cloud/i), { target: { value: "FPT Smart Cloud customers" } });
    fireEvent.click(screen.getByRole("button", { name: "Review research question" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Gemini is temporarily unavailable");
    expect(startManagedResearch).not.toHaveBeenCalled();
    expect(createChatConversation).not.toHaveBeenCalled();
  });

  it("does not start research when the edited question is empty", async () => {
    renderHandoff();
    fireEvent.click(screen.getByRole("button", { name: "Additional capabilities" }));
    fireEvent.click(screen.getByRole("button", { name: /Deep Research/ }));
    fireEvent.change(screen.getByPlaceholderText(/investigate about FPT Smart Cloud/i), { target: { value: "Expansion in Japan" } });
    fireEvent.click(screen.getByRole("button", { name: "Review research question" }));
    await screen.findByTestId("deep-research-brief");
    fireEvent.click(screen.getByRole("button", { name: "Edit question" }));
    fireEvent.change(screen.getByRole("textbox", { name: "Research question" }), { target: { value: " " } });
    fireEvent.click(screen.getByRole("button", { name: "Done editing" }));
    fireEvent.click(screen.getByRole("button", { name: "Start Deep Research" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Enter one research question");
    expect(startManagedResearch).not.toHaveBeenCalled();
    expect(createChatConversation).not.toHaveBeenCalled();
  });

  it("cancels an investigation brief without creating a conversation or job", async () => {
    renderHandoff();

    fireEvent.click(screen.getByRole("button", { name: "Additional capabilities" }));
    fireEvent.click(screen.getByRole("button", { name: /Deep Research/ }));
    fireEvent.change(screen.getByPlaceholderText(/investigate about FPT Smart Cloud/i), { target: { value: "Expansion in Japan" } });
    fireEvent.click(screen.getByRole("button", { name: "Review research question" }));

    await screen.findByTestId("deep-research-brief");
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(screen.queryByTestId("deep-research-brief")).not.toBeInTheDocument();
    expect(screen.getByDisplayValue("Expansion in Japan")).toBeInTheDocument();
    expect(createChatConversation).not.toHaveBeenCalled();
    expect(startManagedResearch).not.toHaveBeenCalled();
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
    renderHandoff();

    await waitFor(() => expect(screen.getByRole("button", { name: "Add research" })).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "Add research" }));
    expect(screen.getByText("Recent expansion")).toBeInTheDocument();
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
    renderHandoff();

    await waitFor(() => expect(screen.getByRole("button", { name: "Add research" })).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "Add research" }));
    fireEvent.click(screen.getByRole("button", { name: "Add" }));

    await waitFor(() => expect(screen.getByLabelText("Attached research context")).toBeInTheDocument());
    expect(attachResearchContext).toHaveBeenCalledWith("company-1", "investigation-1", "conversation-1");
    expect(screen.getByLabelText("Attached research context")).toHaveTextContent("Recent expansion");

    fireEvent.click(screen.getByRole("button", { name: "Remove Recent expansion context" }));
    await waitFor(() => expect(removeResearchContext).toHaveBeenCalledWith("company-1", "investigation-1", "conversation-1"));
    await waitFor(() => expect(screen.queryByLabelText("Attached research context")).not.toBeInTheDocument());
  });
});
