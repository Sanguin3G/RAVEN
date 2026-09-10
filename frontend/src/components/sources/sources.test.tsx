import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { CandidateSourceCard } from "./CandidateSourceCard";
import { EvidenceCard } from "./EvidenceCard";
import { ResearchActivity } from "./ResearchActivity";
import { SourceBadge } from "./SourceBadge";
import { SourceIcon } from "./SourceIcon";

describe("source presentation primitives", () => {
  it("uses a real known favicon and rejects unsafe icon URLs", () => {
    render(<SourceIcon domain="topcv.vn" iconUrl="javascript:alert(1)" kind="TopCv" />);

    expect(screen.getByRole("img", { name: "TopCV source icon" })).toBeInTheDocument();
    expect(document.querySelector('img[src*="google.com/s2/favicons"]')).toHaveAttribute("src", expect.stringContaining("domain=topcv.vn"));
    expect(document.querySelector('img[src^="javascript:"]')).not.toBeInTheDocument();
  });

  it("maps provider identities to safe recognisable favicon hosts", () => {
    const { rerender } = render(<SourceIcon kind="LinkedIn" />);
    expect(document.querySelector('img[src*="domain=linkedin.com"]')).toBeInTheDocument();

    rerender(<SourceIcon kind="Gemini" />);
    expect(document.querySelector('img[src*="domain=ai.google.dev"]')).toBeInTheDocument();

    rerender(<SourceIcon kind="BusinessRegistry" />);
    expect(document.querySelector('img[src*="domain=dangkykinhdoanh.gov.vn"]')).toBeInTheDocument();
  });

  it("uses the company host when no provider icon is available", () => {
    render(<SourceIcon kind="OfficialWebsite" domain="https://www.example.com/about" />);

    expect(document.querySelector('img[src*="domain=example.com"]')).toBeInTheDocument();
  });

  it("renders source badges with human-readable source identity", () => {
    render(<SourceBadge kind="BusinessRegistry" recommended />);

    expect(screen.getByText(/business registry/i)).toBeInTheDocument();
    expect(screen.getByText(/business registry/i)).toHaveAttribute("data-source-kind", "BusinessRegistry");
  });

  it("uses a native labelled checkbox and reports selection changes", () => {
    const onSelectionChange = vi.fn();
    render(
      <CandidateSourceCard
        candidate={{
          id: "candidate-1",
          kind: "OfficialWebsite",
          selected: false,
          title: "About the company",
          url: "https://example.com/about",
          recommended: true,
          recommendationReasons: ["Official domain", "About page"],
        }}
        onSelectionChange={onSelectionChange}
      />,
    );

    const checkbox = screen.getByRole("checkbox", { name: /About the company/i });
    expect(checkbox).not.toBeChecked();
    expect(screen.getByText("Recommended")).toBeInTheDocument();
    expect(screen.getByText("Official domain · About page")).toBeInTheDocument();

    fireEvent.click(checkbox);
    expect(onSelectionChange).toHaveBeenCalledWith(true);
    expect(screen.getByRole("link", { name: "Open source" })).toHaveAttribute("rel", "noreferrer noopener");
  });

  it("shows truthful activity stage and counters without invented percentages", () => {
    render(
      <ResearchActivity
        counters={{ crawlCompleted: 2, crawlFailed: 1, crawlTotal: 3, documentsAdded: 2 }}
        stage="acquiring"
      />,
    );

    expect(screen.getByRole("status")).toHaveTextContent("Acquire selected sources");
    expect(screen.getByText("2 / 3")).toBeInTheDocument();
    expect(screen.getByText("Crawls failed")).toBeInTheDocument();
    expect(screen.queryByText(/%/)).not.toBeInTheDocument();
  });

  it("surfaces acquisition failures and duplicate evidence facts", () => {
    render(
      <>
        <CandidateSourceCard
          candidate={{
            acquisitionMessage: "Crawler timed out",
            acquisitionStatus: "failed",
            id: "candidate-failed",
            selected: true,
            title: "Unavailable source",
            url: "https://example.com/failure",
          }}
          onSelectionChange={vi.fn()}
        />
        <EvidenceCard
          evidence={{
            id: "evidence-duplicate",
            status: "duplicate",
            statusMessage: "Equivalent content already stored",
            title: "Duplicate source",
            url: "https://example.com/duplicate",
          }}
        />
      </>,
    );

    expect(screen.getByText(/Acquisition failed — Crawler timed out/)).toBeInTheDocument();
    expect(screen.getByText("Duplicate content skipped")).toBeInTheDocument();
    expect(screen.getByText("Equivalent content already stored")).toBeInTheDocument();
  });
});
