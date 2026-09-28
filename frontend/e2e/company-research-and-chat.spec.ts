import { expect, test } from "@playwright/test";

const companyId = "44444444-4444-4444-4444-444444444444";
const profile = {
  id: "profile-1", companyId, researchRunId: "run-1", generatedAt: "2026-09-10T00:00:00Z", confirmedAt: "2026-09-10T00:00:00Z", version: 1,
  legalName: "Northwind Research Company", website: "https://northwind.example", country: "Vietnam", headquarters: null, registrationNumberOrTaxId: null,
  foundedYear: null, primaryIndustry: "Research", secondaryIndustries: [], companySize: null, employeeCount: null, employeeCountRange: null,
  summary: "A research company.", productsServices: [], markets: [], leadership: [], locations: [], publicLinks: [], evidence: [], validationWarnings: [],
};
const company = { id: companyId, name: "Northwind Research", website: profile.website, country: profile.country, legalName: profile.legalName, registrationNumber: null, headquarters: null, createdAt: "2026-09-01T00:00:00Z", updatedAt: "2026-09-10T00:00:00Z", lastResearchedAt: "2026-09-10T00:00:00Z", archivedAt: null };
const settings = {
  groundingMode: "Auto", profileModel: "gemini-3.5-flash-lite", chatModel: "gemini-3.5-flash-lite", groundingModel: "gemini-3.5-flash-lite",
  deepResearchModel: "gemini-3.8-flash", aiSourceRerankingEnabled: true, providerPreset: "LocalFirst",
  searchProviderPriority: ["brave"], crawlerProviderPriority: ["crawl4ai-local"],
  customSearchProviderPriority: ["brave", "exa"], customCrawlerProviderPriority: ["crawl4ai-local", "exa"],
  managedResearchProvider: "exa-agent", managedResearchDepth: "Adaptive", updatedAt: "2026-09-10T00:00:00Z",
};
const providerStatus = {
  brave: { provider: "brave", configured: false }, crawl4Ai: { provider: "crawl4ai-local", configured: false },
  exa: { provider: "exa", configured: false }, gemini: { provider: "gemini", configured: false }, deepResearchModel: settings.deepResearchModel,
};
const providerHealth = { generatedAt: "2026-09-28T00:00:00Z", models: [], recentActivity: [] };
const researchRun = { id: "run-1", companyId, status: "Searching", requestedSearchProvider: "brave", actualSearchProvider: "brave", requestedCrawlerProvider: "crawl4ai-local", actualCrawlerProvider: null, sourcesFound: 1, sourcesSelected: 0, sourcesCrawled: 0, startedAt: "2026-09-10T00:00:00Z", completedAt: null, error: null, stage: "AwaitingSourceSelection", researchHint: null, queriesTotal: 1, queriesCompleted: 1, uniqueCandidates: 1, recommendedCandidates: 1, crawlTotal: 0, crawlCompleted: 0, crawlSucceeded: 0, crawlFailed: 0, documentsAdded: 0, duplicatesSkipped: 0 };
const sourceCandidate = { id: "candidate-1", researchRunId: researchRun.id, url: "https://northwind.example/about", normalizedUrl: "https://northwind.example/about", domain: "northwind.example", title: "About Northwind", snippet: "Company overview", sourceKind: "OfficialWebsite", recommendationReasons: ["Official domain"], recommended: true, selected: false, acquisitionStatus: "Pending", acquisitionError: null, iconUrl: null, discoveredAt: "2026-09-10T00:00:00Z" };

async function installFixture(page: import("@playwright/test").Page) {
  let webSearchEnabled = false;
  let capabilityUpdates = 0;
  let researchStarts = 0;
  const streamedQuestions: string[] = [];
  let messages: object[] = [];
  await page.route((url) => url.pathname.startsWith("/api/"), async (route) => {
    const { pathname } = new URL(route.request().url());
    const request = route.request();
    const method = request.method();
    const question = pathname.endsWith("/messages/stream") && request.method() === "POST"
      ? (request.postDataJSON() as { question?: string }).question?.toLowerCase() ?? ""
      : "";
    if (pathname.endsWith("/messages/stream") && method === "POST") streamedQuestions.push(question);
    const response = question.includes("latest")
      ? { conversationId: "conversation-1", messageId: `message-web-${streamedQuestions.length}`, companyId, profileVersion: 1, status: "Answered", answer: "Northwind's latest public update was published in September 2026.", citations: [{ origin: "Web", webEvidenceSnapshotId: "snapshot-1", title: "Northwind public update", url: "https://northwind.example/news", retrievedAt: "2026-09-19T00:00:00Z" }], webEvidenceSnapshots: [{ id: "snapshot-1", url: "https://northwind.example/news", title: "Northwind public update", searchSnippet: "September update", contentExcerpt: "Northwind published its latest update.", searchProvider: "fake-search", crawlerProvider: "fake-crawler", searchRank: 1, retrievedAt: "2026-09-19T00:00:00Z" }], toolExecutions: [{ tool: "search_web", provider: "fake-search", status: "succeeded", durationMs: 1 }], followUpQuestion: null }
      : question.includes("ceo")
      ? { conversationId: "conversation-1", messageId: "message-ceo", companyId, profileVersion: 1, status: "Answered", answer: "The accepted profile identifies the CEO as Jane Doe.", citations: [{ origin: "Profile", sourceDocumentId: "source-1", fieldPath: "leadership[0]", title: "Northwind leadership", url: "https://northwind.example/leadership", retrievedAt: "2026-09-10T00:00:00Z" }], webEvidenceSnapshots: [], toolExecutions: [], followUpQuestion: null }
      : question.includes("research")
        ? { conversationId: "conversation-1", messageId: "message-research", companyId, profileVersion: 1, status: "Guidance", answer: "Deep Research can investigate this across multiple sources. Open Investigations to continue.", citations: [], webEvidenceSnapshots: [], toolExecutions: [], followUpQuestion: null }
        : { conversationId: "conversation-1", messageId: "message-hello", companyId, profileVersion: 1, status: "Conversational", answer: "Hello. Ask me about this company's accepted profile and stored evidence.", citations: [], webEvidenceSnapshots: [], toolExecutions: [], followUpQuestion: null };
    const conversation = { id: "conversation-1", companyId, profileVersionId: profile.id, profileVersion: 1, webSearchEnabled, title: "Ask RAVEN", createdAt: "2026-09-10T00:00:00Z", updatedAt: "2026-09-10T00:00:00Z", messages };
    if (pathname.endsWith("/messages/stream") && method === "POST") {
      messages = [...messages,
        { id: `user-${messages.length}`, role: "User", content: question, status: "Completed", citations: [], webEvidenceSnapshots: [], toolExecutions: [], createdAt: "2026-09-19T00:00:00Z" },
        { id: response.messageId, role: "Assistant", content: response.answer, status: "Completed", answerStatus: response.status, citations: response.citations, webEvidenceSnapshots: response.webEvidenceSnapshots, toolExecutions: response.toolExecutions, createdAt: "2026-09-19T00:00:00Z" },
      ];
      await route.fulfill({ contentType: "text/event-stream", body: `event: progress\ndata: {"stage":"Analyzing","message":"Analyzing the question","completed":null,"total":null}\n\nevent: completed\ndata: ${JSON.stringify(response)}\n\n` });
      return;
    }
    if (pathname.endsWith("/capabilities") && method === "PATCH") {
      capabilityUpdates++;
      webSearchEnabled = (request.postDataJSON() as { webSearchEnabled: boolean }).webSearchEnabled;
      await route.fulfill({ contentType: "application/json", body: JSON.stringify({ ...conversation, webSearchEnabled }) });
      return;
    }
    if (pathname.includes("/managed-research") && method === "POST") researchStarts++;
    const systemBody = pathname.endsWith("/settings/research") ? settings
      : pathname.endsWith("/settings/ai-models") ? { projectAvailabilityVerified: false, message: "", models: [] }
        : pathname.endsWith("/system/provider-status") ? providerStatus
          : pathname.endsWith("/system/provider-health") ? providerHealth : null;
    if (systemBody) {
      await route.fulfill({ contentType: "application/json", body: JSON.stringify(systemBody) });
      return;
    }
    const body = pathname.endsWith("/chat/conversations") && route.request().method() === "POST"
      ? conversation
      : pathname.endsWith("/chat/conversations/conversation-1")
        ? { ...conversation, webSearchEnabled, messages }
        : pathname.endsWith(`/companies/${companyId}/profile`) ? profile
          : pathname.endsWith(`/companies/${companyId}/profile/versions`) ? [profile]
            : pathname.endsWith(`/companies/${companyId}`) ? company
              : pathname.endsWith(`/companies/${companyId}/coverage`) ? { companyId, researchRunId: null, items: [], budgetExhausted: false }
                : pathname.endsWith(`/companies/${companyId}/monitoring`) ? { companyId, enabled: false, cadence: "Weekly", nextRunAt: null, lastRunAt: null, lastRunStatus: null }
                  : [];
    await route.fulfill({ contentType: "application/json", body: JSON.stringify(body) });
  });
  return { capabilityUpdates: () => capabilityUpdates, researchStarts: () => researchStarts, streamedQuestions: () => [...streamedQuestions] };
}

async function installResearchFixture(page: import("@playwright/test").Page, identity: object) {
  let discoveryStarts = 0;
  await page.route((url) => url.pathname.startsWith("/api/"), async (route) => {
    const { pathname } = new URL(route.request().url());
    const method = route.request().method();
    let body: object = [];
    if (pathname.endsWith("/settings/research")) body = { groundingMode: "Auto" };
    else if (pathname.endsWith("/research/identity/resolve")) body = identity;
    else if (pathname.endsWith("/companies/matches")) body = [];
    else if (pathname.endsWith("/companies") && method === "POST") body = company;
    else if (pathname.endsWith("/research/start")) { discoveryStarts++; body = researchRun; }
    else if (pathname.endsWith(`/research-runs/${researchRun.id}`)) body = researchRun;
    else if (pathname.endsWith(`/research-runs/${researchRun.id}/candidates`)) body = [sourceCandidate];
    else if (pathname.endsWith("/execution")) body = { summary: { totalWallClockDurationMs: 1, searchCalls: 1, crawlCalls: 0, aiCalls: 0, providerAttempts: 1, fallbacks: 0, inputTokens: null, outputTokens: null }, operations: [{ category: "Search", operation: "web_search", status: "Completed", provider: "Brave", durationMs: 1 }] };
    await route.fulfill({ contentType: "application/json", body: JSON.stringify(body) });
  });
  return { discoveryStarts: () => discoveryStarts };
}

test("Ask RAVEN conversation and truthful capability menu", async ({ page }) => {
  const fixture = await installFixture(page);
  await page.goto(`/companies/${companyId}`);
  await expect(page.getByRole("complementary", { name: "Ask RAVEN assistant" })).toBeVisible();
  await page.getByRole("textbox", { name: /Ask about/ }).fill("hi");
  await page.getByRole("button", { name: "Send question" }).click();
  await expect(page.getByText("Hello. Ask me about this company's accepted profile")).toBeVisible();
  await expect(page).toHaveURL(/conversation=conversation-1/);
  await expect(page.getByRole("textbox", { name: /Ask about/ })).toBeEnabled();

  await page.getByRole("textbox", { name: /Ask about/ }).fill("Who is the CEO?");
  await expect(page.getByRole("button", { name: "Send question" })).toBeEnabled();
  await page.getByRole("button", { name: "Send question" }).click();
  await expect(page.getByText("The accepted profile identifies the CEO as Jane Doe.")).toBeVisible();
  await page.getByRole("button", { name: "Sources" }).click();
  await expect(page.getByRole("link", { name: /Northwind leadership/ })).toBeVisible();

  await page.getByRole("button", { name: "Search latest" }).click();
  const followUp = page.getByRole("textbox", { name: "Follow-up question" });
  await expect(followUp).toHaveValue("Search current public information about: Who is the CEO?");
  expect(fixture.capabilityUpdates()).toBe(0);
  expect(fixture.streamedQuestions()).toHaveLength(2);
  await page.getByRole("button", { name: "Cancel" }).click();
  expect(fixture.capabilityUpdates()).toBe(0);

  await page.getByRole("button", { name: "Search latest" }).click();
  await followUp.fill("What is the latest public update?");
  await page.getByRole("button", { name: "Search", exact: true }).click();
  await expect(page.getByText(/latest public update was published/)).toBeVisible();
  expect(fixture.capabilityUpdates()).toBe(1);
  expect(fixture.streamedQuestions()).toHaveLength(3);
  await expect(page.getByRole("button", { name: "Show web sources" })).toHaveCount(0);
  await page.getByRole("button", { name: "Sources" }).last().click();
  await expect(page.getByRole("link", { name: /Northwind public update/ })).toBeVisible();

  await page.getByRole("button", { name: "Research further" }).click();
  await expect(page.getByRole("textbox", { name: "Research objective" })).toHaveValue("Investigate in depth: What is the latest public update?");
  expect(fixture.researchStarts()).toBe(0);
  await page.getByRole("button", { name: "Cancel" }).click();
  expect(fixture.researchStarts()).toBe(0);

  await page.getByRole("textbox", { name: /Ask about/ }).fill("Can you research this company more deeply?");
  await page.getByRole("button", { name: "Send question" }).click();
  await expect(page.getByText(/Deep Research can investigate this/)).toBeVisible();

  await page.getByRole("button", { name: "Additional capabilities" }).click();
  await page.getByRole("button", { name: "Add research context" }).click();
  await expect(page.getByRole("region", { name: "Research context picker" })).toBeVisible();
  await page.getByRole("button", { name: "Back to additional capabilities" }).click();
  await expect(page.getByRole("checkbox", { name: "Web search" })).toBeChecked();
  await expect(page.getByText("On · current public information")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("button", { name: "Additional capabilities" })).toHaveAttribute("aria-expanded", "false");

  await page.reload();
  await expect(page.getByText("Web", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Additional capabilities" }).click();
  await expect(page.getByRole("checkbox", { name: "Web search" })).toBeChecked();
  await page.keyboard.press("Escape");
  await page.getByRole("tab", { name: "Investigations" }).click();
  await expect(page.getByRole("tab", { name: "Investigations" })).toHaveAttribute("aria-selected", "true", { timeout: 10_000 });
  await expect(page.getByRole("textbox", { name: /Ask about/ })).toBeVisible();
  expect(fixture.researchStarts()).toBe(0);
});

test("company workspace tabs remain usable", async ({ page }) => {
  await installFixture(page);
  await page.goto(`/companies/${companyId}`);
  for (const tab of ["Overview", "Sources", "Investigations", "Changes", "Monitoring"]) {
    await page.getByRole("tab", { name: tab }).click();
    await expect(page.getByRole("tab", { name: tab })).toHaveAttribute("aria-selected", "true");
  }
});

test("Companies always opens the list while contextual return restores the company route", async ({ page }) => {
  await installFixture(page);
  const companyRoute = `/companies/${companyId}?tab=investigations&conversation=conversation-1`;
  const navigation = page.getByRole("navigation", { name: "Primary navigation" });
  await page.goto(companyRoute);
  await navigation.getByRole("link", { name: "Settings" }).click();
  await expect(page).toHaveURL("http://127.0.0.1:5173/settings");
  const returnLink = page.getByRole("link", { name: "Back to Northwind Research" });
  await expect(returnLink).toBeVisible();
  await page.reload();
  await expect(returnLink).toBeVisible();
  await returnLink.click();
  await expect(page).toHaveURL(`http://127.0.0.1:5173${companyRoute}`);

  await navigation.getByRole("link", { name: "Settings" }).click();
  await navigation.getByRole("link", { name: "Companies" }).click();
  await expect(page).toHaveURL("http://127.0.0.1:5173/companies");
  await navigation.getByRole("link", { name: "Settings" }).click();
  await expect(page.getByRole("link", { name: /Back to Northwind Research/ })).toHaveCount(0);

  await page.goto(companyRoute);
  await navigation.getByRole("link", { name: "System status" }).click();
  await expect(page).toHaveURL("http://127.0.0.1:5173/status");
  await page.getByRole("link", { name: "Back to Northwind Research" }).click();
  await expect(page).toHaveURL(`http://127.0.0.1:5173${companyRoute}`);
});

test("resolved identity starts one discovery phase with canonical execution details", async ({ page }) => {
  const fixture = await installResearchFixture(page, { status: "Resolved", ambiguityType: "None", recommendedEntityId: "northwind", entities: [{ temporaryId: "northwind", displayName: "Northwind Research", legalName: null, country: "Vietnam", region: null, officialDomain: "northwind.example", entityType: "Company", parentTemporaryId: null, relationshipToQuery: "Exact", confidence: "High", shortDescription: null }], requestedHints: [], message: null, resolutionMethod: "ModelKnowledge" });
  await page.goto("/companies/new");
  await page.getByLabel(/Company name/).fill("Northwind Research");
  await page.getByRole("button", { name: "Research public sources" }).click();
  await expect(page.getByRole("heading", { name: "Review source candidates" })).toBeVisible();
  await page.getByText(/Execution details/).click();
  await expect(page.getByText("web_search")).toBeVisible();
  await expect(page.getByText("SearchRequested")).toHaveCount(0);
  expect(fixture.discoveryStarts()).toBe(1);
});

test("family identity selection starts one discovery phase", async ({ page }) => {
  const fixture = await installResearchFixture(page, { status: "Ambiguous", ambiguityType: "CorporateFamily", recommendedEntityId: null, entities: [{ temporaryId: "fpt", displayName: "FPT Corporation", legalName: null, country: "Vietnam", region: null, officialDomain: "fpt.com.vn", entityType: "ParentGroup", parentTemporaryId: null, relationshipToQuery: "Exact", confidence: "High", shortDescription: "Parent group" }, { temporaryId: "fpt-software", displayName: "FPT Software", legalName: null, country: "Vietnam", region: null, officialDomain: "fptsoftware.com", entityType: "Subsidiary", parentTemporaryId: "fpt", relationshipToQuery: "Subsidiary", confidence: "High", shortDescription: "Technology services subsidiary" }], requestedHints: [], message: null, resolutionMethod: "ModelKnowledge" });
  await page.goto("/companies/new");
  await page.getByLabel(/Company name/).fill("FPT");
  await page.getByRole("button", { name: "Research public sources" }).click();
  await expect(page.getByRole("heading", { name: "Which organization do you mean?" })).toBeVisible();
  await page.getByRole("radio", { name: /FPT Software/ }).check();
  await page.getByRole("button", { name: "Continue with selected organization" }).click();
  await expect(page.getByRole("heading", { name: "Review source candidates" })).toBeVisible();
  expect(fixture.discoveryStarts()).toBe(1);
});

test("Ask RAVEN remains usable at 390 by 844", async ({ page }) => {
  await installFixture(page);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(`/companies/${companyId}`);
  const assistant = page.getByRole("complementary", { name: "Ask RAVEN assistant" });
  await expect(assistant).toBeVisible();
  await page.getByRole("button", { name: "Additional capabilities" }).click();
  await expect(page.getByRole("button", { name: "Add research context" })).toBeVisible();
  await page.screenshot({ path: "output/playwright/ask-raven-mobile.png" });
});
