import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AskRavenHandoff } from "./AskRavenHandoff";

describe("AskRavenHandoff", () => {
  const props = {
    companyId: "company-1",
    companyName: "FPT Smart Cloud",
    profileVersion: 2,
    sourceCount: 17,
    lastResearchedAt: "2026-09-11T09:00:00Z",
  };

  it("keeps the assistant company-scoped and truthful while the backend is pending", () => {
    render(<AskRavenHandoff {...props} />);

    expect(screen.getByRole("heading", { name: "Ask RAVEN" })).toBeInTheDocument();
    expect(screen.getByText("FPT Smart Cloud")).toBeInTheDocument();
    expect(screen.getByText("Integration pending")).toBeInTheDocument();
    expect(screen.getByText("RAVEN already has this company context")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Send" })).toBeDisabled();
  });

  it("switches between Quick and Deep presentation without creating a fake answer", () => {
    render(<AskRavenHandoff {...props} />);

    const deepButton = screen.getByRole("button", { name: /Deep Research/ });
    fireEvent.click(deepButton);

    expect(deepButton).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByLabelText("What should RAVEN investigate?")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Start research" })).toBeDisabled();
    expect(screen.queryByText(/Preparing answer|Found .* candidates/)).not.toBeInTheDocument();
  });

  it("does not submit an invented response", () => {
    render(<AskRavenHandoff {...props} />);
    const composer = screen.getByLabelText("Ask about FPT Smart Cloud");

    fireEvent.change(composer, { target: { value: "Who leads this company?" } });
    fireEvent.click(screen.getByRole("button", { name: "Send" }));

    expect(screen.getByRole("status")).toHaveTextContent("Your question was not sent");
    expect(screen.queryByText("Nguyen Van A")).not.toBeInTheDocument();
    expect(screen.queryByText("Found 12 candidates")).not.toBeInTheDocument();
  });
});
