import { expect, test } from "@playwright/test";

const companyId = "99999999-9999-9999-9999-999999999999";
const company = { id: companyId, name: "Northwind Investigations", website: "https://northwind.example", country: "Vietnam", legalName: "Northwind Investigations Co.", registrationNumber: null, headquarters: "17 Duy Tan Street, Cau Giay, Hanoi, Vietnam", createdAt: "2026-09-01T00:00:00Z", updatedAt: "2026-09-18T00:00:00Z", lastResearchedAt: null, archivedAt: null };
const profile = { id: "profile-day9", companyId, researchRunId: "run-day9", generatedAt: "2026-09-18T00:00:00Z", confirmedAt: "2026-09-18T00:00:00Z", version: 1, legalName: company.legalName, website: company.website, country: company.country, headquarters: company.headquarters, registrationNumberOrTaxId: null, foundedYear: null, primaryIndustry: "Research", secondaryIndustries: [], companySize: null, employeeCount: null, employeeCountRange: null, summary: "A research company.", productsServices: [], markets: [], leadership: [], locations: [], publicLinks: [], evidence: [], validationWarnings: [] };
const artifact = {
  id: "artifact-day9",
  companyId,
  title: "Japan expansion",
  question: "How has Northwind expanded in Japan?",
  objective: "How has Northwind expanded in Japan?",
  summary: "Northwind is building a partner network in Japan.",
  result: "Original imported response preserved here.",
  rawResponse: "Original imported response preserved here.",
  createdAt: "2026-09-18T00:00:00Z",
  researchType: "Fast",
  sourceCount: 2,
  sourceDocumentIds: [],
  origin: "ExternalImport",
  claims: [{ field: "Markets", statement: "Northwind announced a Japan partnership.", supportingSourceLeadIds: ["lead-1"] }],
  sourceLeads: [{ id: "lead-1", title: "Northwind partnership", url: "https://northwind.example/japan", publisher: "Northwind" }],
  uncertainties: ["The partnership's current operating scale is not yet verified."],
};

async function installDay9Fixture(page: import("@playwright/test").Page) {
  let analysisPolls = 0;
  await page.route((url) => url.pathname.startsWith("/api/"), async (route) => {
    const { pathname } = new URL(route.request().url());
    const method = route.request().method();
    let body: unknown = [];
    if (pathname.endsWith(`/companies/${companyId}/profile`)) body = profile;
    else if (pathname.endsWith(`/companies/${companyId}/profile/versions`)) body = [profile];
    else if (pathname.endsWith(`/companies/${companyId}`)) body = company;
    else if (pathname.endsWith(`/companies/${companyId}/coverage`)) body = { companyId, researchRunId: null, items: [{ target: "Leadership", level: "Missing", supportingSourceCount: 0, strongestSourceKind: null, reasons: [] }], budgetExhausted: false };
    else if (pathname.endsWith(`/companies/${companyId}/monitoring`)) body = { companyId, enabled: false, cadence: "Weekly", nextRunAt: null, lastRunAt: null, lastRunStatus: null };
    else if (pathname.endsWith(`/companies/${companyId}/saved-research`)) body = [artifact];
    else if (pathname.endsWith(`/companies/${companyId}/saved-research/${artifact.id}/organization`)) body = { id: "organization-day9", savedResearchArtifactId: artifact.id, version: 1, createdAt: "2026-09-18T00:00:00Z", executiveSummary: "Organized Japan expansion findings.", themes: [{ name: "Japan partnerships", summary: "Partner activity is the main theme.", claimCount: 1 }], evidenceGaps: ["Current operating scale"], suggestedFollowUps: ["Verify the partnership's current status."], uncertainties: artifact.uncertainties, isHumanEdited: false };
    else if (pathname.endsWith("/external-research/brief")) body = { objective: "Current leadership", focusedTargets: ["Leadership"], markdown: "Company:\nNorthwind Investigations\n\nTarget:\nCurrent leadership\n\nFocus only on current senior leadership and supporting URLs." };
    else if (pathname.endsWith("/external-research/analyze") && method === "POST") body = { id: "analysis-day9", companyId, question: "Current leadership", status: "Queued", createdAt: "2026-09-18T00:00:00Z" };
    else if (pathname.includes("/external-research/analyze/")) {
      analysisPolls += 1;
      body = analysisPolls < 5
        ? { id: "analysis-day9", companyId, question: "Current leadership", status: "Analyzing", createdAt: "2026-09-18T00:00:00Z" }
        : { id: "analysis-day9", companyId, question: "Current leadership", status: "Completed", createdAt: "2026-09-18T00:00:00Z", completedAt: "2026-09-18T00:01:00Z", result: { summary: "Jane Doe is the current regional CEO.", claims: [{ field: "Leadership", statement: "Jane Doe became regional CEO in January 2026.", supportingSourceLeadIds: ["lead-1"] }], sourceLeads: [{ id: "lead-1", title: "Leadership page", url: "https://northwind.example/leadership", publisher: "Northwind" }], uncertainties: ["A secondary source may be outdated."], suggestedFollowUps: [], rawMarkdown: "## Research Summary" } };
    } else if (pathname.endsWith("/external-research/import") && method === "POST") body = { ...artifact, title: "Current leadership", question: "Current leadership" };
    else if (pathname.endsWith("/research/verify-source-leads") && method === "POST") body = { id: "run-verify-day9", companyId, status: "Queued", stage: "Discovering", documentsAdded: 1 };
    else if (pathname.endsWith("/chat/conversations") && method === "POST") body = { id: "conversation-day9", companyId, profileVersionId: profile.id, profileVersion: 1, title: null, createdAt: "2026-09-18T00:00:00Z", updatedAt: "2026-09-18T00:00:00Z", messages: [] };
    await route.fulfill({ contentType: "application/json", body: JSON.stringify(body) });
  });
}

test("external research assist is focused, resumable, and reviewable", async ({ page }) => {
  await installDay9Fixture(page);
  await page.goto(`/companies/${companyId}`);

  await page.getByRole("button", { name: "Find leadership" }).click();
  await page.getByRole("button", { name: /External AI Assist/ }).click();
  await expect(page.getByRole("heading", { name: /External AI Assist.*Leadership/ })).toBeVisible();
  await expect(page.getByRole("textbox", { name: "Copyable prompt" })).toHaveValue(/Current leadership/);
  await page.getByRole("button", { name: "Copy prompt" }).click();
  await expect(page.getByRole("button", { name: "Copied" })).toBeVisible();

  await page.getByRole("button", { name: /I've got a response/ }).click();
  await page.getByRole("textbox", { name: "Assistant response" }).fill("## Research Summary\n\nJane Doe became regional CEO in January 2026.\n\n## Claims\n\n### Claim 1\nTarget: Leadership\nStatement: Jane Doe became regional CEO in January 2026.\nSources:\n- https://northwind.example/leadership\n\n## Uncertainties\n\n- A secondary source may be outdated.");
  await page.getByRole("button", { name: "Analyze" }).click();
  await page.getByRole("button", { name: "Minimize External AI Assist" }).click();
  await expect(page.getByRole("button", { name: /External AI Assist/ })).toBeVisible();

  await page.getByRole("button", { name: /External AI Assist/ }).click();
  await expect(page.getByRole("heading", { name: "Review imported research" })).toBeVisible({ timeout: 10_000 });
  await expect(page.getByText("Unverified research material")).toBeVisible();
  await expect(page.getByRole("heading", { name: "RAVEN review" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Verify selected sources" })).not.toBeVisible();
  await page.getByRole("button", { name: "Save to Investigation" }).click();
  await expect(page.getByText("Saved to Investigation as unverified research material.")).toBeVisible();
});
test("investigation workspace keeps organized findings and raw material accessible", async ({ page }) => {
  await installDay9Fixture(page);
  await page.goto(`/companies/${companyId}`);
  await page.getByRole("tab", { name: "Investigations" }).click();

  const workspace = page.getByTestId("dossier-investigations");
  await expect(workspace.getByRole("heading", { name: "Japan expansion" })).toBeVisible();
  await expect(workspace.getByText("Organized Japan expansion findings.")).toBeVisible();
  await expect(workspace.getByRole("heading", { name: "Claims" })).toBeVisible();
  await workspace.getByRole("heading", { name: "Claims" }).click();
  await expect(workspace.getByText("Northwind announced a Japan partnership.")).toBeVisible();
  await workspace.getByRole("heading", { name: "Contradictions / uncertainties" }).click();
  await expect(workspace.getByText("The partnership's current operating scale is not yet verified.")).toBeVisible();
  await workspace.getByText("Raw research material / activity").click();
  await expect(workspace.getByText("Original imported response preserved here.")).toBeVisible();
  await workspace.getByRole("button", { name: "Organize investigations" }).click();
  await expect(workspace.getByText("Organized Japan expansion findings.")).toBeVisible();

  await workspace.getByRole("button", { name: "Improve profile" }).click();
  await expect(page.getByRole("heading", { name: "Review this investigation for the profile" })).toBeVisible();
  await expect(page.getByRole("tab", { name: "Investigations" })).toHaveAttribute("aria-selected", "true");
  await page.getByRole("button", { name: "Close targeted research" }).click();
  await workspace.getByRole("button", { name: /Deep Research/ }).click();
  await expect(page.getByRole("tab", { name: "Investigations" })).toHaveAttribute("aria-selected", "true");
});

test.describe("mobile research workspace", () => {
  test.use({ viewport: { width: 390, height: 844 } });

  test("workspace and external assist remain usable at 390 by 844", async ({ page }) => {
    await installDay9Fixture(page);
    await page.goto(`/companies/${companyId}`);
    await page.getByRole("button", { name: "Collapse assistant" }).click();
    await page.getByRole("tab", { name: "Investigations" }).click();
    await expect(page.getByTestId("dossier-investigations").getByRole("heading", { name: "Japan expansion" })).toBeVisible();
    await page.screenshot({ path: "test-results/day9-mobile-workspace.png", fullPage: true });

    await page.getByRole("tab", { name: "Overview" }).click();
    await page.getByRole("button", { name: "Find leadership" }).click();
    await page.getByRole("button", { name: /External AI Assist/ }).click();
    await expect(page.getByRole("heading", { name: /External AI Assist.*Leadership/ })).toBeVisible();
    await page.screenshot({ path: "test-results/day9-mobile-external-assist.png", fullPage: true });
  });
});
