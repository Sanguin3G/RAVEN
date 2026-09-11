import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, it, vi } from "vitest";
import { SettingsPage } from "./SettingsPage";
import { jsonResponse, renderWithRouter } from "../test/test-utils";

const settings = {
  groundingMode: "Auto",
  profileModel: "gemini-3.5-flash-lite",
  groundingModel: "gemini-3.5-flash-lite",
  deepResearchModel: "gemini-3.8-flash",
  aiSourceRerankingEnabled: true,
  providerPreset: "LocalFirst",
  searchProviderPriority: ["brave"],
  crawlerProviderPriority: ["crawl4ai-local"],
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

  expect(await screen.findByRole("heading", { name: "Research intelligence" })).toBeInTheDocument();
  expect(screen.getByRole("radio", { name: /Auto/ })).toBeChecked();
  expect(screen.getByLabelText("Identity grounding")).toHaveValue("gemini-3.5-flash-lite");
  expect(screen.getAllByText("Configured")).toHaveLength(3);

  await user.click(screen.getByRole("radio", { name: /^Always/ }));
  await user.selectOptions(screen.getByLabelText("Identity grounding"), "gemini-3.8-flash");
  expect(screen.getByText("You have unsaved changes.")).toBeInTheDocument();

  await user.click(screen.getByRole("button", { name: "Save changes" }));
  await waitFor(() => expect(screen.getByText("Research settings saved.")).toBeInTheDocument());
  expect(savedBody).toEqual({
    groundingMode: "Always",
    profileModel: settings.profileModel,
    groundingModel: "gemini-3.8-flash",
    deepResearchModel: settings.deepResearchModel,
    aiSourceRerankingEnabled: settings.aiSourceRerankingEnabled,
    providerPreset: settings.providerPreset,
    searchProviderPriority: settings.searchProviderPriority,
    crawlerProviderPriority: settings.crawlerProviderPriority,
  });
  expect(screen.getByText("All research settings saved.")).toBeInTheDocument();
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
  await screen.findByRole("heading", { name: "Research intelligence" });
  await user.click(screen.getByRole("radio", { name: /^Off/ }));
  expect(screen.getByText("You have unsaved changes.")).toBeInTheDocument();

  await user.click(screen.getByRole("button", { name: "Reset defaults" }));
  await waitFor(() => expect(screen.getByText("Research settings reset to defaults.")).toBeInTheDocument());
  expect(screen.getByRole("radio", { name: /^Off/ })).toBeChecked();
  expect(screen.getByRole("switch", { name: "AI source recommendations" })).not.toBeChecked();
  expect(fetchMock).toHaveBeenCalledWith("/api/settings/research/reset", expect.objectContaining({ method: "POST" }));
});
