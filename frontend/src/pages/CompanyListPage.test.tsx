import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { CompanyListPage } from "./CompanyListPage";
import { jsonResponse, renderWithRouter } from "../test/test-utils";

const firstId = "11111111-1111-1111-1111-111111111111";
const duplicateId = "22222222-2222-2222-2222-222222222222";
const sparseId = "33333333-3333-3333-3333-333333333333";

const companies = [
  { id: firstId, name: "FPT Software", website: "https://fptsoftware.com", country: "Vietnam", createdAt: "2026-09-01T00:00:00Z", updatedAt: "2026-09-10T00:00:00Z", lastResearchedAt: "2026-09-10T00:00:00Z", archivedAt: null },
  { id: duplicateId, name: "FPT Software", website: "https://fptsoftware.com", country: "Vietnam", createdAt: "2026-09-02T00:00:00Z", updatedAt: "2026-09-09T00:00:00Z", lastResearchedAt: null, archivedAt: null },
  { id: sparseId, name: "Disposable Test Company", website: null, country: null, createdAt: "2026-09-03T00:00:00Z", updatedAt: "2026-09-08T00:00:00Z", lastResearchedAt: null, archivedAt: null },
];

const health = (companyId: string, status: string) => ({ companyId, status, baseStatus: status, flags: [], hasAcceptedProfile: status !== "Unresearched", coveredTargetCount: status === "Complete" ? 9 : 0, requiredTargetCount: 9, sourceDocumentCount: status === "Complete" ? 8 : 0, researchRunCount: status === "Complete" ? 2 : 0, lastResearchedAt: null, missingTargets: status === "Complete" ? [] : ["Leadership"], reasons: [] });

const review = {
  companies: [
    { companyId: firstId, name: "FPT Software", health: health(firstId, "Complete") },
    { companyId: duplicateId, name: "FPT Software", health: health(duplicateId, "PossibleDuplicate") },
    { companyId: sparseId, name: "Disposable Test Company", health: health(sparseId, "Unresearched") },
  ],
  duplicateGroups: [{ groupId: "website-fpt", strongestMatch: "WebsiteHost", matchTypes: ["WebsiteHost"], rationale: "Exact normalized website host match.", members: [
    { companyId: firstId, name: "FPT Software", website: "https://fptsoftware.com", country: "Vietnam" },
    { companyId: duplicateId, name: "FPT Software", website: "https://fptsoftware.com", country: "Vietnam" },
  ] }],
  recommendations: [{ kind: "SparseProfile", companyId: sparseId, title: "Company has not been researched", summary: "No accepted profile or research evidence is recorded.", relatedCompanyIds: [], reasons: ["No accepted profile"], healthStatus: "Unresearched", isAiGenerated: false }],
  aiUsed: false,
  aiWarning: null,
};

const mergePreview = {
  canonicalCompanyId: firstId,
  duplicateCompanyId: duplicateId,
  canonicalCompanyName: "FPT Software",
  duplicateCompanyName: "FPT Software",
  researchRuns: 3,
  researchCandidates: 5,
  researchEvents: 4,
  identityCandidates: 0,
  sourceDocuments: 8,
  duplicateSourceDocumentsToReuse: 2,
  duplicateSourceDocumentsToMove: 1,
  profileCandidates: 1,
  profileVersions: 3,
  profileEvidenceRows: 6,
  profileChanges: 2,
  deepResearchRuns: 1,
  deepResearchActivities: 2,
  savedInvestigations: 1,
  hasCanonicalMonitoring: true,
  hasDuplicateMonitoring: false,
  warnings: ["2 duplicate source document(s) match canonical URL/content and will be deduplicated."],
};

function installApi(overrides: (url: string, init?: RequestInit) => Response | undefined = () => undefined) {
  return vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
    const url = String(input);
    const custom = overrides(url, init);
    if (custom) return custom;
    if (url.endsWith("/api/companies") && (!init?.method || init.method === "GET")) return jsonResponse(companies);
    if (url.endsWith("/workspace-review")) return jsonResponse(review);
    if (url.endsWith("/merge/preview")) return jsonResponse(mergePreview);
    if (url.endsWith("/merge/confirm")) return jsonResponse({ canonicalCompany: companies[0], preview: mergePreview });
    if (url.endsWith("/archive")) return jsonResponse({ ...companies[0], archivedAt: "2026-09-11T00:00:00Z" });
    if (url.endsWith("/restore")) return jsonResponse({ ...companies[0], archivedAt: null });
    return jsonResponse({});
  });
}

beforeEach(() => {
  installApi();
});

it("shows profile health and filters records by health", async () => {
  const user = userEvent.setup();
  renderWithRouter(<CompanyListPage />, "/companies");

  expect((await screen.findAllByText("Complete")).length).toBeGreaterThan(0);
  expect(screen.getByRole("columnheader", { name: "Monitoring" })).toBeInTheDocument();
  expect(screen.getAllByText("Not configured").length).toBeGreaterThan(0);
  expect(screen.getAllByText("Unresearched").length).toBeGreaterThan(0);
  await user.selectOptions(screen.getByLabelText("Profile health"), "Unresearched");
  expect(screen.getByRole("link", { name: "Disposable Test Company" })).toBeInTheDocument();
  expect(screen.queryAllByRole("link", { name: "FPT Software" })).toHaveLength(0);
});

it("archives a company from the row action menu", async () => {
  const user = userEvent.setup();
  const fetchMock = installApi((url, init) => url.endsWith(`/api/companies/${firstId}/archive`) && init?.method === "POST"
    ? jsonResponse({ ...companies[0], archivedAt: "2026-09-11T00:00:00Z" }) : undefined);
  renderWithRouter(<CompanyListPage />, "/companies");

  expect((await screen.findAllByRole("link", { name: "FPT Software" })).length).toBe(2);
  await user.click(screen.getAllByRole("button", { name: "Actions for FPT Software" })[0]);
  await user.click(screen.getByRole("menuitem", { name: "Archive" }));
  await waitFor(() => expect(screen.getAllByRole("link", { name: "FPT Software" })).toHaveLength(1));
  expect(fetchMock).toHaveBeenCalledWith(`/api/companies/${firstId}/archive`, expect.objectContaining({ method: "POST" }));
});

it("requires deliberate confirmation before permanent deletion", async () => {
  const user = userEvent.setup();
  const fetchMock = installApi((url, init) => url.endsWith(`/api/companies/${sparseId}`) && init?.method === "DELETE" ? new Response(null, { status: 204 }) : undefined);
  renderWithRouter(<CompanyListPage />, "/companies");

  await screen.findByRole("link", { name: "Disposable Test Company" });
  await user.click(screen.getAllByRole("button", { name: "Actions for Disposable Test Company" })[0]);
  await user.click(screen.getByRole("menuitem", { name: "Delete permanently" }));
  expect(screen.getByRole("dialog")).toHaveTextContent("Delete company permanently?");
  expect(screen.getByRole("dialog")).toHaveTextContent("Disposable Test Company");
  await user.click(screen.getByRole("button", { name: "Delete permanently" }));
  await waitFor(() => expect(screen.queryByRole("link", { name: "Disposable Test Company" })).not.toBeInTheDocument());
  expect(fetchMock).toHaveBeenCalledWith(`/api/companies/${sparseId}`, expect.objectContaining({ method: "DELETE", body: JSON.stringify({ confirm: true }) }));
});

it("reviews workspace duplicates and confirms a human-readable merge", async () => {
  const user = userEvent.setup();
  const fetchMock = installApi();
  renderWithRouter(<CompanyListPage />, "/companies");

  await user.click(await screen.findByRole("button", { name: /Review workspace/i }));
  expect(await screen.findByText("Possible duplicates")).toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: "Review merge" }));
  expect(await screen.findByRole("dialog")).toHaveTextContent("RAVEN will preserve");
  expect(screen.getByRole("dialog")).toHaveTextContent("3 research runs");
  await user.click(screen.getByRole("button", { name: "Merge companies" }));
  await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  expect(fetchMock).toHaveBeenCalledWith("/api/companies/merge/confirm", expect.objectContaining({ method: "POST", body: JSON.stringify({ canonicalCompanyId: firstId, duplicateCompanyId: duplicateId, confirm: true }) }));
});
