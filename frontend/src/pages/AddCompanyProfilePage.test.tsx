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

const resolvedIdentity = {
  status: "Resolved",
  ambiguityType: "None",
  recommendedEntityId: "fpt-software",
  entities: [{
    temporaryId: "fpt-software", displayName: "FPT Software", legalName: null, country: "Vietnam", region: null,
    officialDomain: "fptsoftware.com", entityType: "Subsidiary", parentTemporaryId: null,
    relationshipToQuery: "Exact", confidence: "High", shortDescription: null,
  }],
  requestedHints: [], message: null, resolutionMethod: "ModelKnowledge",
};

const execution = {
  summary: { totalWallClockDurationMs: 0, searchCalls: 0, crawlCalls: 0, aiCalls: 0, providerAttempts: 0, fallbacks: 0, inputTokens: null, outputTokens: null },
  operations: [],
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
    if (url.endsWith("/execution")) return jsonResponse(execution);
    if (url.endsWith("/api/research/identity/resolve")) return jsonResponse(resolvedIdentity);
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
    if (url.endsWith("/execution")) return jsonResponse(execution);
    if (url.endsWith("/api/research/identity/resolve")) return jsonResponse(resolvedIdentity);
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
    if (url.endsWith("/execution")) return Promise.resolve(jsonResponse(execution));
    if (url.endsWith("/api/research/identity/resolve")) return Promise.resolve(jsonResponse(resolvedIdentity));
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
    temporaryId: "fpt-software",
    displayName: "FPT Software",
    legalName: "FPT Software Company Limited",
    country: "Vietnam",
    officialDomain: "fptsoftware.com",
    entityType: "Subsidiary",
    relationshipToQuery: "Subsidiary",
    confidence: "High",
    shortDescription: "Technology services subsidiary of FPT Corporation",
  };
  const ambiguousIdentity = { status: "Ambiguous", ambiguityType: "CorporateFamily", recommendedEntityId: null, entities: [{ ...identityCandidate, entityType: "Subsidiary", parentTemporaryId: "fpt" }, { temporaryId: "fpt", displayName: "FPT Corporation", legalName: null, country: "Vietnam", region: null, officialDomain: "fpt.com.vn", entityType: "ParentGroup", parentTemporaryId: null, relationshipToQuery: "Exact", confidence: "High", shortDescription: "Parent group" }], requestedHints: ["Country", "Website", "RegistrationNumber"], message: "Several organizations could match.", resolutionMethod: "ModelKnowledge" };
  const fetchMock = vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
    const url = String(input);
    if (url.endsWith("/execution")) return jsonResponse(execution);
    if (url.endsWith("/api/research/identity/resolve")) {
      const identityRequest = JSON.parse(String(init?.body || "{}"));
      if (identityRequest.guidedRefinement) return jsonResponse({ ...ambiguousIdentity, requestedHints: ["ResearchHint", "Country"], message: "The current name describes a family, so a business description or country would distinguish the member you mean." });
      return jsonResponse(ambiguousIdentity);
    }
    if (url.endsWith("/api/companies/matches")) return jsonResponse([]);
    if (url.endsWith("/api/companies") && init?.method === "POST") return jsonResponse(company, 201);
    if (url.endsWith("/research/start")) return jsonResponse(run);
    if (url.endsWith("/candidates")) return jsonResponse([candidate]);
    return jsonResponse([]);
  });

  renderWithRouter(<AddCompanyProfilePage />, "/companies/new");
  await user.type(screen.getByLabelText(/Company name/), "FPT");
  await user.click(screen.getByRole("button", { name: "Research public sources" }));

  expect(await screen.findByRole("heading", { name: "Which organization do you mean?" })).toBeInTheDocument();
  expect(screen.getByText("Technology services subsidiary of FPT Corporation")).toBeInTheDocument();
  expect(screen.getByText("Still can’t find the right organization?")).toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: "Still can’t find the right organization?" }));
  const guidanceDialog = await screen.findByRole("dialog");
  expect(guidanceDialog).toHaveTextContent("Refine this identity search");
  expect(guidanceDialog).toHaveTextContent("WHY THIS SEARCH NEEDS MORE SIGNAL");
  expect(guidanceDialog).toHaveTextContent(/business description or country would distinguish/);
  expect(guidanceDialog).toHaveTextContent("What the company does");
  expect(guidanceDialog).toHaveTextContent("Country or region");
  await user.click(screen.getByRole("button", { name: "Minimize guidance" }));
  await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  expect(screen.getByRole("heading", { name: "Which organization do you mean?" })).toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: "View guidance" }));
  expect(await screen.findByRole("dialog")).toHaveTextContent("Refine this identity search");
  await user.click(screen.getByRole("button", { name: "Back to original choices" }));
  await screen.findByRole("heading", { name: "Which organization do you mean?" });
  await user.click(screen.getByRole("radio", { name: /FPT Software/ }));
  await user.click(screen.getByRole("button", { name: "Continue with selected organization" }));

  await waitFor(() => expect(screen.getByRole("heading", { name: "Review source candidates" })).toBeInTheDocument());
  expect(fetchMock).not.toHaveBeenCalledWith(expect.stringContaining("/identity/select"), expect.anything());
});

it("keeps an unresolved identity result compact instead of opening a second search form", async () => {
  const user = userEvent.setup();
  vi.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
    const url = String(input);
    if (url.endsWith("/execution")) return jsonResponse(execution);
    if (url.endsWith("/api/research/identity/resolve")) return jsonResponse({
      status: "NeedsMoreInfo",
      ambiguityType: "Unclear",
      recommendedEntityId: null,
      entities: [],
      requestedHints: ["Country", "Website"],
      message: "RAVEN couldn't confidently resolve this organization right now.",
      resolutionMethod: "ModelKnowledge",
    });
    return jsonResponse([]);
  });

  renderWithRouter(<AddCompanyProfilePage />, "/companies/new");
  await user.type(screen.getByLabelText(/Company name/), "Viettel");
  await user.click(screen.getByRole("button", { name: "Research public sources" }));

  expect(await screen.findByRole("heading", { name: "A little more information will help" })).toBeInTheDocument();
  expect(screen.queryByRole("textbox", { name: "Company name" })).not.toBeInTheDocument();
  expect(screen.getByText("Official website")).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Edit search details" })).toBeInTheDocument();

  await user.click(screen.getByRole("button", { name: "Edit search details" }));
  expect(screen.getByRole("textbox", { name: "Company name" })).toHaveValue("Viettel");
});

it("keeps guided advice beside the original form and can reset a new research", async () => {
  const user = userEvent.setup();
  vi.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
    const url = String(input);
    if (url.endsWith("/execution")) return jsonResponse(execution);
    if (url.endsWith("/api/research/identity/resolve")) return jsonResponse({
      status: "Ambiguous",
      ambiguityType: "CorporateFamily",
      recommendedEntityId: null,
      entities: [{
        temporaryId: "fpt",
        displayName: "FPT Corporation",
        legalName: null,
        country: "Vietnam",
        region: null,
        officialDomain: "fpt.com.vn",
        entityType: "ParentGroup",
        parentTemporaryId: null,
        relationshipToQuery: "Exact",
        confidence: "High",
        shortDescription: "Parent group",
      }, {
        temporaryId: "fpt-software",
        displayName: "FPT Software",
        legalName: null,
        country: "Vietnam",
        region: null,
        officialDomain: "fptsoftware.com",
        entityType: "Subsidiary",
        parentTemporaryId: "fpt",
        relationshipToQuery: "Subsidiary",
        confidence: "High",
        shortDescription: "Technology services subsidiary",
      }],
      requestedHints: ["ResearchHint", "Country"],
      message: "A business description or country would distinguish the member you mean.",
      resolutionMethod: "ModelKnowledge",
    });
    return jsonResponse([]);
  });

  renderWithRouter(<AddCompanyProfilePage />, "/companies/new");
  await user.type(screen.getByLabelText(/Company name/), "FPT");
  await user.click(screen.getByRole("button", { name: "Research public sources" }));
  await screen.findByRole("heading", { name: "Which organization do you mean?" });
  await user.click(screen.getByRole("button", { name: "Still can’t find the right organization?" }));
  await screen.findByRole("heading", { name: "Refine this identity search" });
  await user.click(screen.getByRole("button", { name: "Edit search details" }));

  expect(screen.getByText("GUIDED SEARCH ADVICE")).toBeInTheDocument();
  expect(screen.getByText(/business description or country would distinguish/)).toBeInTheDocument();
  expect(screen.getByRole("textbox", { name: "Company name" })).toHaveValue("FPT");

  await user.click(screen.getByRole("button", { name: "Reset form" }));
  expect(screen.getByRole("textbox", { name: "Company name" })).toHaveValue("");
  expect(screen.queryByText("GUIDED SEARCH ADVICE")).not.toBeInTheDocument();
});

it("sends an explicit one-run identity-resolution preference while keeping the default visible", async () => {
  const user = userEvent.setup();
  const fetchMock = vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
    const url = String(input);
    if (url.endsWith("/execution")) return jsonResponse(execution);
    if (url.endsWith("/api/research/identity/resolve")) return jsonResponse(resolvedIdentity);
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
