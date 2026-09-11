import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ProfileChangesPanel } from "./ProfileChangesPanel";
import { ProfileHistoryPanel } from "./ProfileHistoryPanel";
import type { ProfileChange } from "../../api/profileTracking";
import type { CompanyProfileVersion } from "../../types/profile";

const version = (number: number, confirmedAt: string): CompanyProfileVersion => ({
  id: `profile-${number}`,
  companyId: "company-1",
  researchRunId: `run-${number}`,
  generatedAt: confirmedAt,
  confirmedAt,
  version: number,
  displayName: "Example Company",
  secondaryIndustries: [],
  productsServices: [],
  markets: [],
  leadership: [],
  locations: [],
  publicLinks: [],
  evidence: [],
  validationWarnings: [],
});

describe("ProfileHistoryPanel", () => {
  it("shows newest versions first and exposes a real refresh callback", () => {
    const onRefreshResearch = vi.fn();
    const onSelectVersion = vi.fn();

    render(
      <ProfileHistoryPanel
        versions={[version(1, "2026-09-09T10:00:00Z"), version(2, "2026-09-10T10:00:00Z")]}
        currentVersion={2}
        onRefreshResearch={onRefreshResearch}
        onSelectVersion={onSelectVersion}
      />,
    );

    expect(screen.getByText("v2")).toBeInTheDocument();
    expect(screen.getByText("v1")).toBeInTheDocument();
    expect(screen.getByText("Current")).toBeInTheDocument();
    expect(screen.getAllByText(/Confirmed/)).toHaveLength(2);
    expect(screen.getByRole("button", { name: /v2/i })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /refresh research/i }));
    fireEvent.click(screen.getByRole("button", { name: /v2/i }));
    expect(onRefreshResearch).toHaveBeenCalledOnce();
    expect(onSelectVersion).toHaveBeenCalledWith(expect.objectContaining({ version: 2 }));
  });

  it("explains an empty history without inventing a version", () => {
    render(<ProfileHistoryPanel versions={[]} />);

    expect(screen.getByTestId("profile-history-empty")).toHaveTextContent("No accepted profile versions yet");
  });
});

describe("ProfileChangesPanel", () => {
  it("renders a latest-versus-previous diff with readable values", () => {
    const changes: ProfileChange[] = [
      {
        id: "change-1",
        companyId: "company-1",
        fromVersion: 1,
        toVersion: 2,
        fieldPath: "companySize",
        changeType: "Changed",
        oldValueJson: JSON.stringify("1,001–5,000"),
        newValueJson: JSON.stringify("5,001–10,000"),
        detectedAt: "2026-09-10T12:00:00Z",
      },
      {
        id: "change-2",
        companyId: "company-1",
        fromVersion: 1,
        toVersion: 2,
        fieldPath: "markets",
        itemKey: "South Korea",
        changeType: "Added",
        oldValueJson: null,
        newValueJson: JSON.stringify("South Korea"),
        detectedAt: "2026-09-10T12:00:00Z",
      },
    ];

    render(<ProfileChangesPanel changes={changes} versions={[version(1, "2026-09-09T10:00:00Z"), version(2, "2026-09-10T10:00:00Z")]} />);

    expect(screen.getByRole("heading", { name: "Changes from v1 to v2" })).toBeInTheDocument();
    expect(screen.getByText("companySize")).toBeInTheDocument();
    expect(screen.getByText("1,001–5,000")).toBeInTheDocument();
    expect(screen.getByText("5,001–10,000")).toBeInTheDocument();
    expect(screen.getByText("markets · South Korea")).toBeInTheDocument();
  });

  it("handles a first version, no-change state, and malformed JSON", () => {
    const first = version(1, "2026-09-09T10:00:00Z");
    const malformed: ProfileChange = {
      id: "change-malformed",
      companyId: "company-1",
      fromVersion: 1,
      toVersion: 2,
      fieldPath: "summary",
      changeType: "Changed",
      oldValueJson: "{not-json",
      newValueJson: JSON.stringify(null),
      detectedAt: "2026-09-10T12:00:00Z",
    };

    const { rerender } = render(<ProfileChangesPanel changes={[]} versions={[first]} />);
    expect(screen.getByTestId("profile-changes-first-version")).toBeInTheDocument();

    rerender(<ProfileChangesPanel changes={[]} versions={[first, version(2, "2026-09-10T10:00:00Z")]} />);
    expect(screen.getByTestId("profile-changes-none")).toHaveTextContent("No changes detected");

    rerender(<ProfileChangesPanel changes={[malformed]} versions={[first, version(2, "2026-09-10T10:00:00Z")]} />);
    expect(screen.getByText("Value unavailable")).toBeInTheDocument();
    expect(screen.getByText("Not available")).toBeInTheDocument();
  });
});
