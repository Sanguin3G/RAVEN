import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";
import { SettingsPage } from "./SettingsPage";
import { jsonResponse, renderWithRouter } from "../test/test-utils";

const settings = {
  groundingMode: "Auto",
  profileModel: "gemini-3.5-flash-lite",
  chatModel: "gemini-3.5-flash-lite",
  groundingModel: "gemini-3.5-flash-lite",
  deepResearchModel: "gemini-3.8-flash",
  aiSourceRerankingEnabled: true,
  providerPreset: "LocalFirst",
  searchProviderPriority: ["brave"],
  crawlerProviderPriority: ["crawl4ai-local"],
  customSearchProviderPriority: ["brave", "exa"],
  customCrawlerProviderPriority: ["crawl4ai-local", "exa"],
  managedResearchProvider: "exa-agent",
  managedResearchDepth: "Adaptive",
  updatedAt: "2026-09-11T00:00:00Z",
} as const;

const providerStatus = {
  brave: { provider: "brave", configured: true, available: true },
  crawl4Ai: { provider: "crawl4ai-local", configured: true, available: true },
  gemini: { provider: "gemini", configured: true, available: true, selectedModel: settings.profileModel },
  deepResearchModel: settings.deepResearchModel,
};

afterEach(() => vi.restoreAllMocks());

it("loads persistent settings and saves the explicit model roles", async () => {
  const user = userEvent.setup();
  let savedBody: unknown;
  vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
    const url = String(input);
    if (url.endsWith("/api/settings/research") && !init?.method) return jsonResponse(settings);
    if (url.endsWith("/api/system/provider-status")) return jsonResponse(providerStatus);
    if (url.endsWith("/api/settings/research") && init?.method === "PUT") {
      savedBody = JSON.parse(String(init.body));
      return jsonResponse({ ...settings, groundingMode: "Always", groundingModel: "gemini-3.8-flash" });
    }
    return jsonResponse({}, 404);
  });

  renderWithRouter(<SettingsPage />, "/settings");

  expect(await screen.findByRole("heading", { name: "Settings" })).toBeInTheDocument();
  expect(screen.getByRole("radio", { name: /Smart matching/ })).toBeChecked();

  await user.click(screen.getByRole("radio", { name: /^Always verify/ }));
  await user.click(screen.getByRole("button", { name: "AI & models" }));
  expect(screen.getByLabelText("Briefings & RAVEN analysis")).toHaveValue("gemini-3.8-flash");
  await user.selectOptions(screen.getByLabelText("Company matching"), "gemini-3.8-flash");
  await user.selectOptions(screen.getByLabelText("Ask RAVEN & research question"), "gemini-3.5-flash");
  await user.selectOptions(screen.getByLabelText("Briefings & RAVEN analysis"), "gemini-3.5-flash");
  expect(screen.getByText("You have unsaved changes.")).toBeInTheDocument();

  await user.click(screen.getByRole("button", { name: "Save changes" }));
  await waitFor(() => expect(screen.getByText("Research settings saved.")).toBeInTheDocument());
  expect(savedBody).toEqual({
    groundingMode: "Always",
    profileModel: settings.profileModel,
    chatModel: "gemini-3.5-flash",
    groundingModel: "gemini-3.8-flash",
    deepResearchModel: "gemini-3.5-flash",
    aiSourceRerankingEnabled: settings.aiSourceRerankingEnabled,
    providerPreset: settings.providerPreset,
    searchProviderPriority: settings.searchProviderPriority,
    crawlerProviderPriority: settings.crawlerProviderPriority,
    customSearchProviderPriority: settings.customSearchProviderPriority,
    customCrawlerProviderPriority: settings.customCrawlerProviderPriority,
    managedResearchProvider: settings.managedResearchProvider,
    managedResearchDepth: settings.managedResearchDepth,
  });
  expect(screen.getByText("All settings saved.")).toBeInTheDocument();
});

it("does not warn about a failed Gemini model that is no longer configured", async () => {
  const flashLiteSettings = { ...settings, deepResearchModel: "gemini-3.5-flash-lite" };
  vi.spyOn(globalThis, "fetch").mockImplementation(async input => {
    const url = String(input);
    if (url.endsWith("/api/settings/research")) return jsonResponse(flashLiteSettings);
    if (url.endsWith("/api/system/provider-status")) return jsonResponse(providerStatus);
    if (url.endsWith("/api/system/provider-health")) return jsonResponse({
      generatedAt: new Date().toISOString(),
      models: [{ provider: "gemini", model: "gemini-3.8-flash", state: "Degraded",
        lastFailureAt: new Date().toISOString(), lastFailureHttpStatus: 503,
        lastFailureCode: "unavailable", lastFailureSummary: "The provider request failed.", requestsLastMinute: 1 }],
      recentActivity: [],
    });
    return jsonResponse({}, 404);
  });

  renderWithRouter(<SettingsPage />, "/settings");

  expect(await screen.findByRole("heading", { name: "Settings" })).toBeInTheDocument();
  await waitFor(() => expect(screen.queryByText(/Gemini recently had a request failure/)).not.toBeInTheDocument());
});

it("resets the draft and persisted settings through the reset endpoint", async () => {
  const user = userEvent.setup();
  const resetSettings = { ...settings, groundingMode: "Off", aiSourceRerankingEnabled: false };
  const fetchMock = vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
    const url = String(input);
    if (url.endsWith("/api/settings/research") && !init?.method) return jsonResponse(settings);
    if (url.endsWith("/api/system/provider-status")) return jsonResponse(providerStatus);
    if (url.endsWith("/api/settings/research/reset")) return jsonResponse(resetSettings);
    return jsonResponse({}, 404);
  });

  renderWithRouter(<SettingsPage />, "/settings");
  await screen.findByRole("heading", { name: "Settings" });
  await user.click(screen.getByRole("radio", { name: /^Deterministic only/ }));
  expect(screen.getByText("You have unsaved changes.")).toBeInTheDocument();

  await user.click(screen.getByRole("button", { name: "Reset defaults" }));
  await waitFor(() => expect(screen.getByText("Research settings reset to defaults.")).toBeInTheDocument());
  expect(screen.getByRole("radio", { name: /^Deterministic only/ })).toBeChecked();
  expect(screen.getByRole("switch", { name: "AI-assisted source ranking" })).not.toBeChecked();
  expect(fetchMock).toHaveBeenCalledWith("/api/settings/research/reset", expect.objectContaining({ method: "POST" }));
});

it("uses the custom editors as the single priority view", async () => {
  const customSettings = {
    ...settings,
    providerPreset: "Custom",
    searchProviderPriority: ["brave", "exa"],
    crawlerProviderPriority: ["crawl4ai-local", "exa"],
    customSearchProviderPriority: ["brave", "exa"],
    customCrawlerProviderPriority: ["crawl4ai-local", "exa"],
  } as const;

  vi.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
    const url = String(input);
    if (url.endsWith("/api/settings/research")) return jsonResponse(customSettings);
    if (url.endsWith("/api/system/provider-status")) return jsonResponse(providerStatus);
    return jsonResponse({}, 404);
  });

  renderWithRouter(<SettingsPage />, "/settings");

  expect(await screen.findByRole("heading", { name: "Settings" })).toBeInTheDocument();
  const user = userEvent.setup();
  await user.click(screen.getByRole("button", { name: "Advanced" }));
  expect(screen.queryByText("Search priority")).not.toBeInTheDocument();
  expect(screen.queryByText("Crawler priority")).not.toBeInTheDocument();
  expect(screen.getByText("Search order")).toBeInTheDocument();
  expect(screen.getByText("Page acquisition order")).toBeInTheDocument();
  expect(screen.queryByText(/Firecrawl/i)).not.toBeInTheDocument();
});

it("explains when the configured Gemini project exposes none of RAVEN's supported models", async () => {
  vi.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
    const url = String(input);
    if (url.endsWith("/api/settings/research")) return jsonResponse(settings);
    if (url.endsWith("/api/system/provider-status")) return jsonResponse(providerStatus);
    if (url.endsWith("/api/settings/ai-models")) return jsonResponse({
      projectAvailabilityVerified: true,
      message: "Google returned no RAVEN-supported Gemini models for this API key/project.",
      models: ["gemini-3.5-flash-lite", "gemini-3.5-flash", "gemini-3.8-flash"].map(id => ({
        id,
        displayName: id === "gemini-3.8-flash" ? "Gemini 3.8 Flash" : id,
        description: "Compatible generation model",
        availability: "Unavailable",
      })),
    });
    return jsonResponse({}, 404);
  });

  renderWithRouter(<SettingsPage />, "/settings");
  await screen.findByRole("heading", { name: "Settings" });
  await userEvent.setup().click(screen.getByRole("button", { name: "AI & models" }));

  expect(screen.getByRole("alert")).toHaveTextContent("No RAVEN-supported Gemini models were listed");
  expect(screen.getByLabelText("Company matching")).toHaveValue("gemini-3.5-flash-lite");
  expect(screen.getAllByRole("option", { name: /Gemini 3.8 Flash · unavailable to this project/ })[0]).toBeDisabled();
});

it("restores the saved custom route after named presets and links to both routing settings", async () => {
  const user = userEvent.setup();
  const savedCustomSettings = {
    ...settings,
    customSearchProviderPriority: ["exa", "brave"],
    customCrawlerProviderPriority: ["exa", "crawl4ai-local"],
  } as const;

  vi.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
    const url = String(input);
    if (url.endsWith("/api/settings/research")) return jsonResponse(savedCustomSettings);
    if (url.endsWith("/api/system/provider-status")) return jsonResponse(providerStatus);
    return jsonResponse({}, 404);
  });

  renderWithRouter(<SettingsPage />, "/settings");
  await screen.findByRole("heading", { name: "Settings" });
  await user.click(screen.getByRole("button", { name: "Research providers" }));
  const routePreview = screen.getByText("Your route").parentElement!;

  await user.click(screen.getByRole("radio", { name: /Local-first/ }));
  expect(screen.getByRole("radio", { name: /Local-first/ })).toBeChecked();
  expect(routePreview).toHaveTextContent(/Search\s*Brave Search\s*Read pages\s*Crawl4AI Local/);
  await user.click(screen.getByRole("radio", { name: /^Custom/ }));
  expect(screen.getByText("Exa Search & Contents → Brave Search")).toBeInTheDocument();
  expect(screen.getByText("Exa Search & Contents → Crawl4AI Local")).toBeInTheDocument();

  await user.click(screen.getByRole("radio", { name: /Cloud-first/ }));
  expect(routePreview).toHaveTextContent(/Search\s*Exa Search & Contents\s*Read pages\s*Exa Search & Contents/);
  await user.click(screen.getByRole("radio", { name: /^Custom/ }));
  expect(screen.getByText("Exa Search & Contents → Brave Search")).toBeInTheDocument();

  await user.click(screen.getByRole("button", { name: /Configure exact provider order in Advanced routing/ }));
  expect(screen.getByRole("heading", { name: "Advanced routing" })).toBeInTheDocument();
  await user.click(screen.getByRole("button", { name: /Back to Research providers/ }));
  expect(screen.getByRole("heading", { name: "Research route" })).toBeInTheDocument();
});

it("uses Resilient as the ordered fallback preset", async () => {
  const user = userEvent.setup();
  vi.spyOn(globalThis, "fetch").mockImplementation(async (input) => {
    const url = String(input);
    if (url.endsWith("/api/settings/research")) return jsonResponse(settings);
    if (url.endsWith("/api/system/provider-status")) return jsonResponse(providerStatus);
    return jsonResponse({}, 404);
  });

  renderWithRouter(<SettingsPage />, "/settings");
  await screen.findByRole("heading", { name: "Settings" });
  await user.click(screen.getByRole("button", { name: "Research providers" }));

  await user.click(screen.getByRole("radio", { name: /Balanced & resilient/ }));

  expect(screen.getByRole("radio", { name: /Balanced & resilient/ })).toBeChecked();
  expect(screen.getByText("Brave Search → Exa Search & Contents")).toBeInTheDocument();
  expect(screen.getByText("Crawl4AI Local → Exa Search & Contents")).toBeInTheDocument();
});
