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

  it("keeps company context compact and reserves a real conversation viewport", () => {
    render(<AskRavenHandoff {...props} />);

    expect(screen.getByRole("heading", { name: "Ask RAVEN" })).toBeInTheDocument();
    expect(screen.getByText(/FPT Smart Cloud.*v2.*17 sources/)).toBeInTheDocument();
    expect(screen.getByLabelText("Ask RAVEN conversation")).toBeInTheDocument();
    expect(screen.getByText("Ask about this company")).toBeInTheDocument();
    expect(screen.getByLabelText("Ask RAVEN mode")).toHaveValue("quick");
    expect(screen.queryByRole("button", { name: "Send question" })).not.toBeInTheDocument();
  });

  it("switches to Deep presentation without creating a fake answer", () => {
    render(<AskRavenHandoff {...props} />);

    fireEvent.change(screen.getByLabelText("Ask RAVEN mode"), { target: { value: "deep" } });

    expect(screen.getByLabelText("What should RAVEN investigate?")).toBeInTheDocument();
    expect(screen.getByText("Deep Research is ready")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Start Deep Research" })).not.toBeInTheDocument();
    expect(screen.queryByText(/Preparing answer|Found .* candidates/)).not.toBeInTheDocument();
  });

  it("does not submit an invented response", () => {
    render(<AskRavenHandoff {...props} />);
    const composer = screen.getByLabelText("Ask about FPT Smart Cloud");

    fireEvent.change(composer, { target: { value: "Who leads this company?" } });
    fireEvent.click(screen.getByRole("button", { name: "Send question" }));

    expect(screen.getByRole("status")).toHaveTextContent("Your question was not sent");
    expect(screen.queryByText("Nguyen Van A")).not.toBeInTheDocument();
    expect(screen.queryByText("Found 12 candidates")).not.toBeInTheDocument();
  });
});
