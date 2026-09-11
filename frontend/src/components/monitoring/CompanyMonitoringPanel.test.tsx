import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { CompanyMonitoringPanel } from "./CompanyMonitoringPanel";
import type { CompanyMonitoring } from "../../api/monitoring";

const monitoring: CompanyMonitoring = {
  companyId: "company-1",
  enabled: true,
  cadence: "Weekly",
  nextRunAt: "2026-09-18T10:00:00Z",
  lastRunAt: "2026-09-11T10:00:00Z",
  lastRunStatus: "ReadyForReview",
};

describe("CompanyMonitoringPanel", () => {
  it("shows schedule state and review-ready messaging", () => {
    render(<CompanyMonitoringPanel monitoring={monitoring} companyName="FPT IS" onResearchNow={vi.fn()} />);

    expect(screen.getByRole("heading", { name: "Monitor FPT IS" })).toBeInTheDocument();
    expect(screen.getByText("On")).toBeInTheDocument();
    expect(screen.getByText("Weekly")).toBeInTheDocument();
    expect(screen.getByTestId("monitoring-review-ready")).toHaveTextContent("New research update available");
    expect(screen.getByRole("button", { name: /research now/i })).toBeEnabled();
  });

  it("submits a changed cadence and invokes manual research through callbacks", () => {
    const onUpdate = vi.fn();
    const onResearchNow = vi.fn();
    render(<CompanyMonitoringPanel monitoring={monitoring} onUpdate={onUpdate} onResearchNow={onResearchNow} />);

    fireEvent.change(screen.getByLabelText("Frequency"), { target: { value: "Daily" } });
    fireEvent.click(screen.getByRole("button", { name: /save monitoring/i }));
    fireEvent.click(screen.getByRole("button", { name: /research now/i }));

    expect(onUpdate).toHaveBeenCalledWith({ enabled: true, cadence: "Daily" });
    expect(onResearchNow).toHaveBeenCalledOnce();
  });

  it("keeps manual actions honest when callbacks are not provided", () => {
    render(<CompanyMonitoringPanel monitoring={{ ...monitoring, enabled: false, lastRunStatus: "Failed" }} />);

    expect(screen.getByText("Research failed")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /research now/i })).toBeDisabled();
    expect(screen.getByLabelText("Enable scheduled research")).toBeDisabled();
  });
});
