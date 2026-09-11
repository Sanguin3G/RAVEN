import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { TargetedEnrichmentPanel } from "./TargetedEnrichmentPanel";
import type { DossierCompany, DossierProfile } from "./dossierTypes";

const company: DossierCompany = { id: "company-1", displayName: "Northwind Research", country: "Vietnam" };
const profile: DossierProfile = { id: "profile-1", productsServices: [{ name: "Existing product" }], locations: [{ name: "Hanoi" }], publicLinks: [] };

const run = {
  id: "run-1",
  companyId: company.id,
  status: "Completed",
  requestedSearchProvider: "fixture",
  actualSearchProvider: "fixture",
  requestedCrawlerProvider: "fixture",
  actualCrawlerProvider: "fixture",
  sourcesFound: 1,
  sourcesSelected: 1,
  sourcesCrawled: 1,
  startedAt: "2026-09-11T00:00:00Z",
  completedAt: "2026-09-11T00:00:01Z",
  error: null,
  stage: "AwaitingSourceSelection",
  researchHint: null,
  queriesTotal: 1,
  queriesCompleted: 1,
  uniqueCandidates: 1,
  recommendedCandidates: 1,
  crawlTotal: 0,
  crawlCompleted: 0,
  crawlSucceeded: 0,
  crawlFailed: 0,
  documentsAdded: 0,
  duplicatesSkipped: 0,
  groundingMode: "Off",
  resolvedIdentityCandidateId: null,
  mode: "TargetedEnrichment",
  baseProfileVersionId: "profile-1",
  targets: ["Leadership"],
};

describe("TargetedEnrichmentPanel", () => {
  it("takes a server-owned patch through evidence review and confirmation", async () => {
    const fetchMock = vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
      const url = String(input);
      if (url.endsWith("/research/targeted")) return new Response(JSON.stringify(run), { status: 200 });
      if (url.endsWith("/research-runs/run-1/candidates")) return new Response(JSON.stringify([{ id: "candidate-1", researchRunId: "run-1", url: "https://northwind.example/leadership", normalizedUrl: "https://northwind.example/leadership", domain: "northwind.example", title: "Leadership", snippet: "CEO", sourceKind: "OfficialWebsite", recommendationReasons: ["Leadership coverage"], recommended: true, selected: true, acquisitionStatus: "Pending", acquisitionError: null, iconUrl: null, entityRelationship: "SameEntity", semanticRelevance: "High", semanticPurposes: ["Leadership"], semanticRationale: "Official leadership page", discoveredAt: "2026-09-11T00:00:00Z" }]), { status: 200 });
      if (url.endsWith("/research-runs/run-1/acquire")) return new Response(JSON.stringify({ ...run, stage: "EvidenceReady", documentsAdded: 1 }), { status: 200 });
      if (url.endsWith("/research-runs/run-1/sources")) return new Response(JSON.stringify([{ id: "source-1", companyId: company.id, researchRunId: "run-1", url: "https://northwind.example/leadership", title: "Leadership", sourceDomain: "northwind.example", sourceKind: "OfficialWebsite", retrievedAt: "2026-09-11T00:00:00Z", crawlerProvider: "fixture", contentPreview: "CEO: Ada Example" }]), { status: 200 });
      if (url.endsWith("/research-runs/run-1/profile-patch/generate")) return new Response(JSON.stringify({ candidateId: "patch-1", researchRunId: "run-1", baseProfileVersionId: "profile-1", allowedTargets: ["Leadership"], changes: [{ fieldPath: "leadership", oldValue: "[]", proposedValue: "[{\"name\":\"Ada Example\",\"title\":\"CEO\"}]", evidenceSourceDocumentIds: ["source-1"] }], warnings: [] }), { status: 200 });
      if (url.endsWith("/research-runs/run-1/profile-patch/confirm")) return new Response(JSON.stringify({ ...profile, id: "profile-2", version: 2, companyId: company.id, researchRunId: "run-1", leadership: [{ name: "Ada Example", title: "CEO" }], publicLinks: [], evidence: [], secondaryIndustries: [], productsServices: profile.productsServices, markets: [], locations: profile.locations, generatedAt: "2026-09-11T00:00:00Z", confirmedAt: "2026-09-11T00:00:00Z", validationWarnings: [] }), { status: 200 });
      return new Response(JSON.stringify({}), { status: 404 });
    });

    const onConfirmed = vi.fn();
    render(<TargetedEnrichmentPanel company={company} profile={profile} initialTargets={["Leadership"]} open onClose={vi.fn()} onConfirmed={onConfirmed} />);

    expect(screen.getByText("What should RAVEN strengthen?")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Find selected information" }));
    await screen.findByText("Review targeted candidates");
    fireEvent.click(screen.getByRole("button", { name: /Acquire 1 evidence root/ }));
    await screen.findByText("Review acquired evidence");
    fireEvent.click(screen.getByRole("button", { name: "Generate profile update" }));
    await screen.findByText("Profile update");
    expect(screen.getByText("Ada Example")).toBeInTheDocument();
    expect(screen.getByText("Unchanged fields")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Confirm profile update" }));
    await waitFor(() => expect(onConfirmed).toHaveBeenCalledTimes(1));
    expect(fetchMock.mock.calls.some(([, requestInit]) => String(requestInit?.body).includes('"candidateId":"patch-1"'))).toBe(true);
    fetchMock.mockRestore();
  });
});
