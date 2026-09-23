import { expect, test } from "@playwright/test";

const companyId = "88888888-8888-8888-8888-888888888888";
const company = { id: companyId, name: "Northwind Research", website: "https://northwind.example", country: "Vietnam", legalName: "Northwind Research Co.", registrationNumber: null, headquarters: "17 Duy Tan Street, Cau Giay, Hanoi, Vietnam", createdAt: "2026-09-01T00:00:00Z", updatedAt: "2026-09-10T00:00:00Z", lastResearchedAt: null, archivedAt: null };
const profile = { id: "profile-day8", companyId, researchRunId: "run-day8", generatedAt: "2026-09-10T00:00:00Z", confirmedAt: "2026-09-10T00:00:00Z", version: 1, legalName: company.legalName, website: company.website, country: company.country, headquarters: company.headquarters, registrationNumberOrTaxId: null, foundedYear: null, primaryIndustry: "Research", secondaryIndustries: [], companySize: null, employeeCount: null, employeeCountRange: null, summary: "A research company.", productsServices: [], markets: [], leadership: [], locations: [], publicLinks: [], evidence: [], validationWarnings: [] };

async function installManagedResearchFixture(page: import("@playwright/test").Page) {
  let jobs: object[] = [];
  let messages: object[] = [];
  await page.route((url) => url.pathname.startsWith("/api/"), async (route) => {
    const { pathname } = new URL(route.request().url());
    const method = route.request().method();
    let body: object = [];
    if (pathname.endsWith(`/companies/${companyId}/profile`)) body = profile;
    else if (pathname.endsWith(`/companies/${companyId}/profile/versions`)) body = [profile];
    else if (pathname.endsWith(`/companies/${companyId}`)) body = company;
    else if (pathname.endsWith(`/companies/${companyId}/coverage`)) body = { companyId, researchRunId: null, items: [], budgetExhausted: false };
    else if (pathname.endsWith(`/companies/${companyId}/monitoring`)) body = { companyId, enabled: false, cadence: "Weekly", nextRunAt: null, lastRunAt: null, lastRunStatus: null };
    else if (pathname.endsWith("/managed-research/preview") && method === "POST") {
      body = { question: "What public evidence describes Northwind Research's expansion in Japan?", contextRevision: "fixture-revision" };
    } else if (pathname.endsWith("/managed-research") && method === "POST") {
      const request = route.request().postDataJSON() as { objective: string; conversationId?: string; answerInChat?: boolean };
      const objective = request.objective;
      body = { id: "job-day8", companyId, objective, provider: "exa-agent", status: "Queued", conversationId: request.conversationId, answerInChat: request.answerInChat, chatMessageId: "research-user-day8", createdAt: "2026-09-18T00:00:00Z" };
      jobs = [body];
      messages = [...messages, { id: "research-user-day8", role: "User", content: objective, status: "Completed", citations: [], webEvidenceSnapshots: [], toolExecutions: [], createdAt: "2026-09-18T00:00:00Z" }];
    } else if (pathname.endsWith("/managed-research")) body = jobs;
    else if (pathname.endsWith(`/companies/${companyId}/investigations`)) body = jobs.map((job) => {
      const managed = job as { id: string; objective: string; status: string };
      return { id: managed.id, companyId, materialKind: "Managed", materialId: managed.id, title: managed.objective, objective: managed.objective, summary: "", origin: "Deep Research", purpose: "GeneralResearch", topics: [], status: "Running", materialUpdatedAt: "2026-09-18T00:00:00Z", profileImprovementLocked: false, provider: "exa-agent", claims: [], sourceLeads: [], uncertainties: [], briefingIds: [] };
    });
    else if (pathname.endsWith("/external-research/brief")) body = { markdown: "# Research Summary\n\nNorthwind notes", evidenceGaps: [] };
    else if (pathname.endsWith("/external-research/import/preview")) body = { summary: "Imported research notes", claims: [{ field: "Markets", statement: "Operates in Vietnam", notes: null }], sourceLeads: [{ id: "lead-1", title: "Northwind", url: "https://northwind.example", publisher: "Northwind" }], uncertainties: [], suggestedFollowUps: [], rawMarkdown: "# Research Summary" };
    else if (pathname.endsWith("/external-research/import")) body = { id: "artifact-day8", title: "External research", question: "Markets", summary: "Imported research notes", result: "Imported research notes", sourceCount: 0, researchType: "Fast", createdAt: "2026-09-18T00:00:00Z" };
    else if (pathname.endsWith("/chat/conversations") && method === "POST") body = { id: "conversation-day8", companyId, profileVersionId: profile.id, profileVersion: 1, webSearchEnabled: false, title: null, createdAt: "2026-09-18T00:00:00Z", updatedAt: "2026-09-18T00:00:00Z", messages };
    else if (pathname.endsWith("/messages/stream") && method === "POST") {
      const response = { conversationId: "conversation-day8", messageId: "message-day8", companyId, profileVersion: 1, status: "Conversational", answer: "Normal Chat remains available.", citations: [], webEvidenceSnapshots: [], toolExecutions: [], followUpQuestion: null };
      messages = [
        { id: "user-day8", role: "User", content: "Hello", status: "Completed", citations: [], webEvidenceSnapshots: [], toolExecutions: [], createdAt: "2026-09-18T00:00:00Z" },
        { id: response.messageId, role: "Assistant", content: response.answer, status: "Completed", answerStatus: response.status, citations: [], webEvidenceSnapshots: [], toolExecutions: [], createdAt: "2026-09-18T00:00:00Z" },
      ];
      await route.fulfill({ contentType: "text/event-stream", body: `event: progress\ndata: {"stage":"Analyzing","message":"Analyzing the question"}\n\nevent: completed\ndata: ${JSON.stringify(response)}\n\n` });
      return;
    }
    else if (pathname.endsWith("/chat/conversations/conversation-day8")) body = { id: "conversation-day8", companyId, profileVersionId: profile.id, profileVersion: 1, webSearchEnabled: false, title: null, createdAt: "2026-09-18T00:00:00Z", updatedAt: "2026-09-18T00:00:00Z", messages };
    await route.fulfill({ contentType: "application/json", body: JSON.stringify(body) });
  });
}

test("managed research and external import remain explicit review workflows", async ({ page }) => {
  await installManagedResearchFixture(page);
  await page.goto(`/companies/${companyId}`);

  await expect(page.getByRole("link", { name: "View address in Google Maps" })).toBeVisible();
  await page.getByRole("button", { name: "Additional capabilities" }).click();
  await page.getByRole("button", { name: /Deep Research/ }).click();
  await expect(page.getByRole("button", { name: "Remove Deep Research capability" })).toBeVisible();
  await page.getByRole("textbox", { name: /Research about/ }).fill("Expansion in Japan");
   await page.getByRole("button", { name: "Review research question" }).click();
  await expect(page.getByTestId("deep-research-brief")).toBeVisible();
   await page.getByRole("button", { name: "Edit question" }).click();
   await page.getByRole("textbox", { name: "Research question" }).fill("Which customers and expansion activities of Northwind Research in Japan are publicly documented?");
  await page.getByRole("button", { name: "Done editing" }).click();
  await page.getByRole("button", { name: "Start Deep Research" }).click();
  await expect(page.getByText(/Deep Research is running/)).toBeVisible();
  await expect(page.getByRole("textbox", { name: /Ask about/ })).toBeEnabled();
  await page.getByRole("textbox", { name: /Ask about/ }).fill("Hello");
  await page.getByRole("button", { name: "Send question" }).click();
  await expect(page.getByText("Normal Chat remains available.")).toBeVisible();

  await page.getByRole("tab", { name: "Investigations" }).click();
   await expect(page.getByTestId("dossier-investigations").getByRole("heading", { name: "Which customers and expansion activities of Northwind Research in Japan are publicly documented?" })).toBeVisible();
  await expect(page.getByLabel("Investigation details").getByText("Running", { exact: true })).toBeVisible();
  await expect(page.getByText("Research is running. Findings will appear here when ready.")).toBeVisible();
});
