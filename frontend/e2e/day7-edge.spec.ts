import { expect, test } from "@playwright/test";

const companyId = "44444444-4444-4444-4444-444444444444";
const profile = {
  id: "profile-1", companyId, researchRunId: "run-1", generatedAt: "2026-09-10T00:00:00Z", confirmedAt: "2026-09-10T00:00:00Z", version: 1,
  legalName: "Northwind Research Company", website: "https://northwind.example", country: "Vietnam", headquarters: null, registrationNumberOrTaxId: null,
  foundedYear: null, primaryIndustry: "Research", secondaryIndustries: [], companySize: null, employeeCount: null, employeeCountRange: null,
  summary: "A research company.", productsServices: [], markets: [], leadership: [], locations: [], publicLinks: [], evidence: [], validationWarnings: [],
};
const company = { id: companyId, name: "Northwind Research", website: profile.website, country: profile.country, legalName: profile.legalName, registrationNumber: null, headquarters: null, createdAt: "2026-09-01T00:00:00Z", updatedAt: "2026-09-10T00:00:00Z", lastResearchedAt: "2026-09-10T00:00:00Z", archivedAt: null };
const researchRun = { id: "run-1", companyId, status: "Searching", requestedSearchProvider: "brave", actualSearchProvider: "brave", requestedCrawlerProvider: "crawl4ai-local", actualCrawlerProvider: null, sourcesFound: 1, sourcesSelected: 0, sourcesCrawled: 0, startedAt: "2026-09-10T00:00:00Z", completedAt: null, error: null, stage: "AwaitingSourceSelection", researchHint: null, queriesTotal: 1, queriesCompleted: 1, uniqueCandidates: 1, recommendedCandidates: 1, crawlTotal: 0, crawlCompleted: 0, crawlSucceeded: 0, crawlFailed: 0, documentsAdded: 0, duplicatesSkipped: 0 };
const sourceCandidate = { id: "candidate-1", researchRunId: researchRun.id, url: "https://northwind.example/about", normalizedUrl: "https://northwind.example/about", domain: "northwind.example", title: "About Northwind", snippet: "Company overview", sourceKind: "OfficialWebsite", recommendationReasons: ["Official domain"], recommended: true, selected: false, acquisitionStatus: "Pending", acquisitionError: null, iconUrl: null, discoveredAt: "2026-09-10T00:00:00Z" };

async function installFixture(page: import("@playwright/test").Page) {
  await page.route((url) => url.pathname.startsWith("/api/"), async (route) => {
    const { pathname } = new URL(route.request().url());
    const request = route.request();
    const question = pathname.endsWith("/messages") && request.method() === "POST"
      ? (request.postDataJSON() as { question?: string }).question?.toLowerCase() ?? ""
      : "";
    const response = question.includes("ceo")
      ? { conversationId: "conversation-1", messageId: "message-ceo", companyId, profileVersion: 1, status: "Answered", answer: "The accepted profile identifies the CEO as Jane Doe.", citations: [{ origin: "Profile", sourceDocumentId: "source-1", fieldPath: "leadership[0]", title: "Northwind leadership", url: "https://northwind.example/leadership", retrievedAt: "2026-09-10T00:00:00Z" }], toolExecutions: [], followUpQuestion: null }
      : question.includes("research")
        ? { conversationId: "conversation-1", messageId: "message-research", companyId, profileVersion: 1, status: "Guidance", answer: "Saved investigations are available in Investigations.", citations: [], toolExecutions: [], followUpQuestion: null }
        : { conversationId: "conversation-1", messageId: "message-hello", companyId, profileVersion: 1, status: "Conversational", answer: "Hello. Ask me about this company's accepted profile and stored evidence.", citations: [], toolExecutions: [], followUpQuestion: null };
    const body = pathname.endsWith("/chat/conversations") && route.request().method() === "POST"
      ? { id: "conversation-1", companyId, profileVersionId: profile.id, profileVersion: 1, title: "Ask RAVEN", createdAt: "2026-09-10T00:00:00Z", updatedAt: "2026-09-10T00:00:00Z", messages: [] }
      : pathname.includes("/chat/conversations/") && pathname.endsWith("/messages")
        ? response
        : pathname.endsWith(`/companies/${companyId}/profile`) ? profile
          : pathname.endsWith(`/companies/${companyId}/profile/versions`) ? [profile]
            : pathname.endsWith(`/companies/${companyId}`) ? company
              : pathname.endsWith(`/companies/${companyId}/coverage`) ? { companyId, researchRunId: null, items: [], budgetExhausted: false }
                : pathname.endsWith(`/companies/${companyId}/monitoring`) ? { companyId, enabled: false, cadence: "Weekly", nextRunAt: null, lastRunAt: null, lastRunStatus: null }
                  : [];
    await route.fulfill({ contentType: "application/json", body: JSON.stringify(body) });
  });
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
  await installFixture(page);
  await page.goto(`/companies/${companyId}`);
  await expect(page.getByRole("complementary", { name: "Ask RAVEN assistant" })).toBeVisible();
  await page.getByRole("textbox", { name: /Ask about/ }).fill("hi");
  await page.getByRole("button", { name: "Send question" }).click();
  await expect(page.getByText("Hello. Ask me about this company's accepted profile")).toBeVisible();

  await page.getByRole("textbox", { name: /Ask about/ }).fill("Who is the CEO?");
  await page.getByRole("button", { name: "Send question" }).click();
  await expect(page.getByText("The accepted profile identifies the CEO as Jane Doe.")).toBeVisible();
  await expect(page.getByRole("link", { name: "Northwind leadership" })).toBeVisible();

  await page.getByRole("textbox", { name: /Ask about/ }).fill("Can you research this company more deeply?");
  await page.getByRole("button", { name: "Send question" }).click();
  await expect(page.getByText(/Saved investigations are available in Investigations/)).toBeVisible();

  await page.getByRole("button", { name: "Additional capabilities" }).click();
  const investigations = page.getByRole("link", { name: /Open Investigations/ });
  await expect(investigations).toBeVisible();
  await expect(page.getByRole("button", { name: /Search the web/ })).toBeDisabled();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("button", { name: "Additional capabilities" })).toHaveAttribute("aria-expanded", "false");
  await page.getByRole("button", { name: "Additional capabilities" }).click();
  await investigations.click();
  await expect(page).toHaveURL(new RegExp(`companies/${companyId}\\?tab=investigations`));
  await expect(page.getByRole("tab", { name: "Investigations" })).toHaveAttribute("aria-selected", "true");
});

test("company workspace tabs remain usable", async ({ page }) => {
  await installFixture(page);
  await page.goto(`/companies/${companyId}`);
  for (const tab of ["Overview", "Sources", "Investigations", "Changes", "Monitoring"]) {
    await page.getByRole("tab", { name: tab }).click();
    await expect(page.getByRole("tab", { name: tab })).toHaveAttribute("aria-selected", "true");
  }
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
  await expect(page.getByRole("link", { name: /Open Investigations/ })).toBeVisible();
  await page.screenshot({ path: "output/playwright/day7-ask-mobile.png" });
});
