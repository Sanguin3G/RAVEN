import { render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { CompanyOverview } from "./CompanyOverview";
import type { DossierCompany, DossierProfile } from "./dossierTypes";

const company: DossierCompany = {
  id: "company-42",
  displayName: "Northwind Research",
  country: "Vietnam",
};

const profile: DossierProfile = {
  headquarters: "17 Duy Tan Street & Tower A, Cầu Giấy, Hanoi",
  locations: [],
  productsServices: [],
  markets: [],
  leadership: [],
  publicLinks: [],
};

describe("CompanyOverview headquarters map", () => {
  beforeEach(() => vi.unstubAllEnvs());
  afterEach(() => { vi.unstubAllEnvs(); vi.unstubAllGlobals(); });

  function mockRuntimeConfig(key: string | null) {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      if (String(input).endsWith("/api/runtime-config")) {
        return new Response(JSON.stringify({ googleMapsEmbedApiKey: key, demoMode: false }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        });
      }
      return new Response("[]", { status: 200, headers: { "Content-Type": "application/json" } });
    }));
  }

  it("does not render a map when no headquarters address is available", () => {
    render(<MemoryRouter><CompanyOverview company={company} profile={{ ...profile, headquarters: null }} /></MemoryRouter>);

    expect(screen.queryByTestId("company-map")).not.toBeInTheDocument();
  });

  it("provides a safe address-only Google Maps link when the embed key is absent", () => {
    mockRuntimeConfig(null);
    render(<MemoryRouter><CompanyOverview company={{ ...company, headquarters: "Hanoi, Vietnam" }} profile={{ ...profile, headquarters: null }} /></MemoryRouter>);

    expect(screen.queryByTitle(/Map for/)).not.toBeInTheDocument();
    const link = screen.getByRole("link", { name: "View address in Google Maps" });
    const url = new URL(link.getAttribute("href") ?? "");
    expect(url.origin).toBe("https://www.google.com");
    expect(url.pathname).toBe("/maps/search/");
    expect(url.searchParams.get("api")).toBe("1");
    expect(url.searchParams.get("query")).toBe("Hanoi, Vietnam");
  });

  it("renders a lazy accessible embed with safely encoded address and runtime key", async () => {
    mockRuntimeConfig("test key");
    render(<MemoryRouter><CompanyOverview company={company} profile={profile} /></MemoryRouter>);

    const frame = await screen.findByTitle("Map for 17 Duy Tan Street & Tower A, Cầu Giấy, Hanoi");
    expect(frame).toHaveAttribute("loading", "lazy");
    expect(frame).toHaveAttribute("referrerpolicy", "no-referrer-when-downgrade");

    const url = new URL(frame.getAttribute("src") ?? "");
    expect(url.origin).toBe("https://www.google.com");
    expect(url.pathname).toBe("/maps/embed/v1/place");
    expect(url.searchParams.get("key")).toBe("test key");
    expect(url.searchParams.get("q")).toBe("17 Duy Tan Street & Tower A, Cầu Giấy, Hanoi");
  });
});
