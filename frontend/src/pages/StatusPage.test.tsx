import { afterEach, expect, it, vi } from "vitest";
import { screen } from "@testing-library/react";
import { StatusPage } from "./StatusPage";
import { jsonResponse, renderWithRouter } from "../test/test-utils";

afterEach(() => vi.restoreAllMocks());

it("shows recent Gemini rate limiting separately from configured credentials", async () => {
  const failureAt = new Date(Date.now() - 2 * 60_000).toISOString();
  vi.spyOn(globalThis, "fetch").mockImplementation(async input => {
    const url = String(input);
    if (url.endsWith("/api/system/provider-status")) return jsonResponse({
      brave: { provider: "brave", configured: true, available: null },
      crawl4Ai: { provider: "crawl4ai-local", configured: true, available: true },
      gemini: { provider: "gemini", configured: true, available: null },
      exa: { provider: "exa", configured: true, available: null },
      deepResearchModel: "gemini-3.8-flash",
    });
    if (url.endsWith("/api/settings/research")) return jsonResponse({
      groundingMode: "Auto", profileModel: "gemini-3.5-flash-lite", groundingModel: "gemini-3.5-flash-lite",
      chatModel: "gemini-3.5-flash-lite", deepResearchModel: "gemini-3.8-flash", aiSourceRerankingEnabled: true, providerPreset: "Resilient",
      searchProviderPriority: ["brave", "exa"], crawlerProviderPriority: ["crawl4ai-local", "exa"],
      managedResearchProvider: "exa-agent", managedResearchDepth: "Adaptive", updatedAt: failureAt,
    });
    if (url.endsWith("/api/system/provider-health")) return jsonResponse({
      generatedAt: new Date().toISOString(),
      models: [{ provider: "gemini", model: "gemini-3.5-flash-lite", state: "Degraded", lastFailureAt: failureAt,
        lastFailureHttpStatus: 429, lastFailureCode: "resource_exhausted", lastFailureSummary: "Rate limited by the provider.", requestsLastMinute: 15 }],
      recentActivity: [{ timestamp: failureAt, provider: "gemini", model: "gemini-3.5-flash-lite", operation: "company_chat", status: "Failed", httpStatus: 429, failureKind: "RateLimited" }],
    });
    if (url.endsWith("/api/health")) return new Response("Healthy", { status: 200 });
    return jsonResponse({}, 404);
  });

  renderWithRouter(<StatusPage />, "/status");

  expect(await screen.findByRole("heading", { name: "System status" })).toBeInTheDocument();
  expect(await screen.findByText(/GEMINI NEEDS ATTENTION/i)).toBeInTheDocument();
  expect(screen.getByRole("heading", { name: "Rate limited" })).toBeInTheDocument();
  expect(screen.getByText(/RAVEN sent 15 requests using this model/)).toBeInTheDocument();
  expect(screen.getByText(/Company matching and source ranking · Company Profile · Ask RAVEN/)).toBeInTheDocument();
  expect(screen.getAllByText("Configured · not used recently").length).toBeGreaterThan(0);
});
