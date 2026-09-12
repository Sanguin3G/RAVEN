import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { AddCompanyProfilePage } from "./AddCompanyProfilePage";
import { jsonResponse, renderWithRouter } from "../test/test-utils";

const company = {
  id: "11111111-1111-1111-1111-111111111111",
  name: "FPT Software",
  legalName: "FPT Software Company Limited",
  registrationNumber: "0101248141",
  headquarters: "Hanoi, Vietnam",
  website: "https://fptsoftware.com",
  country: "Vietnam",
  createdAt: "2026-09-01T00:00:00Z",
  updatedAt: "2026-09-10T00:00:00Z",
  lastResearchedAt: "2026-09-09T00:00:00Z",
};

const run = {
  id: "22222222-2222-2222-2222-222222222222",
  companyId: company.id,
  status: "Searching",
  requestedSearchProvider: "brave",
  actualSearchProvider: "brave",
  requestedCrawlerProvider: "crawl4ai-local",
  actualCrawlerProvider: null,
  sourcesFound: 1,
  sourcesSelected: 0,
  sourcesCrawled: 0,
  startedAt: "2026-09-10T00:00:00Z",
  completedAt: null,
  error: null,
  stage: "AwaitingSourceSelection",
  researchHint: null,
  queriesTotal: 3,
  queriesCompleted: 3,
  uniqueCandidates: 1,
  recommendedCandidates: 1,
  crawlTotal: 0,
  crawlCompleted: 0,
  crawlSucceeded: 0,
  crawlFailed: 0,
  documentsAdded: 0,
  duplicatesSkipped: 0,
};

const candidate = {
  id: "33333333-3333-3333-3333-333333333333",
  researchRunId: run.id,
  url: "https://fptsoftware.com/about",
  normalizedUrl: "https://fptsoftware.com/about",
  domain: "fptsoftware.com",
  title: "About FPT Software",
  snippet: "Company overview",
  sourceKind: "OfficialWebsite",
  recommendationReasons: ["Official company domain", "About/company page"],
  recommended: true,
  selected: false,
  acquisitionStatus: "Pending",
  acquisitionError: null,
  iconUrl: null,
  discoveredAt: "2026-09-10T00:00:00Z",
};

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
});

afterEach(() => {
  vi.restoreAllMocks();
});

it("offers an explicit reuse-or-create decision for a likely duplicate", async () => {
  const user = userEvent.setup();
  const fetchMock = vi.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
    const url = String(input);
    if (url.endsWith("/api/settings/research")) return jsonResponse({ groundingMode: "Auto" });
    if (url.endsWith("/api/companies/matches")) return jsonResponse([
      { company, matchStrength: "Exact", matchReason: "The registration number matches." },
    ]);
    return jsonResponse([]);
  });

  renderWithRouter(<AddCompanyProfilePage />, "/companies/new");
  await user.type(screen.getByLabelText(/Company name/), "FPT Software");
  await user.type(screen.getByLabelText(/Registration \/ tax ID/), "0101248141");
  await user.click(screen.getByRole("button", { name: "Research public sources" }));

  expect(await screen.findByRole("heading", { name: "Existing company found" })).toBeInTheDocument();
  expect(screen.getByText("Exact identity match")).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Research existing company" })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Create separate anyway" })).toBeInTheDocument();
  expect(fetchMock).toHaveBeenCalledWith("/api/companies/matches", expect.objectContaining({ method: "POST", body: expect.stringContaining("0101248141") }));
});

it("preselects recommendations and rejects an empty acquisition selection", async () => {
  const user = userEvent.setup();
  vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
    const url = String(input);
    if (url.endsWith("/api/companies/matches")) return jsonResponse([]);
    if (url.endsWith("/api/companies") && init?.method === "POST") return jsonResponse(company, 201);
    if (url.endsWith("/research/start")) return jsonResponse(run);
    if (url.endsWith("/candidates")) return jsonResponse([candidate]);
    return jsonResponse([]);
  });

  renderWithRouter(<AddCompanyProfilePage />, "/companies/new");
  await user.type(screen.getByLabelText(/Company name/), "FPT Software");
  await user.click(screen.getByRole("button", { name: "Research public sources" }));

  const checkbox = await screen.findByRole("checkbox");
  expect(checkbox).toBeChecked();
  await user.click(checkbox);
  await user.click(screen.getByRole("button", { name: "Acquire 0 selected sources" }));

  expect(await screen.findByRole("alert")).toHaveTextContent("Select at least one discovered source");
  expect(screen.getByText("Review source candidates")).toBeInTheDocument();
});

it("shows truthful discovery activity while the discovery request is pending", async () => {
  const user = userEvent.setup();
  let resolveDiscover!: (response: Response) => void;
  const discoverResponse = new Promise<Response>((resolve) => { resolveDiscover = resolve; });
  vi.spyOn(globalThis, "fetch").mockImplementation((input, init) => {
    const url = String(input);
    if (url.endsWith("/api/companies/matches")) return Promise.resolve(jsonResponse([]));
    if (url.endsWith("/api/companies") && init?.method === "POST") return Promise.resolve(jsonResponse(company, 201));
    if (url.endsWith("/research/start")) return Promise.resolve(jsonResponse({ ...run, stage: "Discovering", status: "Searching" }));
    if (url.match(/\/api\/research-runs\/[^/]+$/)) return discoverResponse;
    return Promise.resolve(jsonResponse([]));
  });

  renderWithRouter(<AddCompanyProfilePage />, "/companies/new");
  await user.type(screen.getByLabelText(/Company name/), "FPT Software");
  await user.click(screen.getByRole("button", { name: "Research public sources" }));

  expect(await screen.findByRole("heading", { name: "Discover public sources" })).toBeInTheDocument();
  expect(screen.getByRole("status")).toHaveTextContent("Research activity: Discover public sources");
  expect(screen.queryByText(/%/)).not.toBeInTheDocument();

  resolveDiscover(jsonResponse(run));
  await waitFor(() => expect(screen.getByRole("heading", { name: "Review source candidates" })).toBeInTheDocument());
});

it("shows ambiguous grounded targets and resumes targeted discovery after selection", async () => {
  const user = userEvent.setup();
  const identityCandidate = {
    id: "44444444-4444-4444-4444-444444444444",
    researchRunId: run.id,
    temporaryId: "fpt-software",
    displayName: "FPT Software",
    legalName: "FPT Software Company Limited",
    country: "Vietnam",
    website: "https://fptsoftware.com",
    officialDomain: "fptsoftware.com",
    entityType: "Subsidiary",
    relationshipHint: "Technology services subsidiary of FPT Corporation",
    confidence: "High",
    rationale: "The official domain and Vietnam signal match the selected technology subsidiary.",
    supportingCandidateIds: [candidate.id],
    recommended: true,
    selected: false,
    createdAt: "2026-09-10T00:00:00Z",
  };
  const targetedRun = { ...run, stage: "AwaitingSourceSelection" };
  const fetchMock = vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
    const url = String(input);
    if (url.endsWith("/api/companies/matches")) return jsonResponse([]);
    if (url.endsWith("/api/companies") && init?.method === "POST") return jsonResponse(company, 201);
    if (url.endsWith("/research/start")) return jsonResponse({ ...run, stage: "AwaitingIdentitySelection" });
    if (url.match(/\/api\/research-runs\/[^/]+$/)) return jsonResponse({ ...run, stage: "AwaitingIdentitySelection" });
    if (url.endsWith("/identity-candidates")) return jsonResponse([identityCandidate]);
    if (url.endsWith("/identity/select")) return jsonResponse(targetedRun);
    if (url.endsWith("/candidates")) return jsonResponse([candidate]);
    return jsonResponse([]);
  });

  renderWithRouter(<AddCompanyProfilePage />, "/companies/new");
  await user.type(screen.getByLabelText(/Company name/), "FPT");
  await user.click(screen.getByRole("button", { name: "Research public sources" }));

  expect((await screen.findAllByRole("heading", { name: "Resolve research target" })).length).toBeGreaterThanOrEqual(1);
  expect(screen.getByText("The official domain and Vietnam signal match the selected technology subsidiary.")).toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: "Research selected company" }));

  await waitFor(() => expect(screen.getByRole("heading", { name: "Review source candidates" })).toBeInTheDocument());
  expect(fetchMock).toHaveBeenCalledWith(`/api/research-runs/${run.id}/identity/select`, expect.objectContaining({ method: "POST", body: JSON.stringify({ candidateId: identityCandidate.id }) }));
});

it("sends an explicit one-run grounding override while keeping the default visible", async () => {
  const user = userEvent.setup();
  const fetchMock = vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
    const url = String(input);
    if (url.endsWith("/api/companies/matches")) return jsonResponse([]);
    if (url.endsWith("/api/companies") && init?.method === "POST") return jsonResponse(company, 201);
    if (url.endsWith("/research/start")) return jsonResponse(run);
    if (url.endsWith("/candidates")) return jsonResponse([candidate]);
    return jsonResponse([]);
  });

  renderWithRouter(<AddCompanyProfilePage />, "/companies/new");
  await user.type(screen.getByLabelText(/Company name/), "FPT Software");
  expect(screen.getByText("Workspace default · Auto")).toBeInTheDocument();
  await user.click(screen.getByRole("radio", { name: /On/ }));
  expect(screen.getByText("One-run override · On")).toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: "Research public sources" }));

  await waitFor(() => expect(screen.getByRole("heading", { name: "Review source candidates" })).toBeInTheDocument());
  expect(fetchMock).toHaveBeenCalledWith(`/api/companies/${company.id}/research/start`, expect.objectContaining({ body: expect.stringContaining('"groundingMode":"Always"') }));
});
