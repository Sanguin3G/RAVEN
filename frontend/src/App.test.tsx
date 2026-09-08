import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it, vi } from "vitest";
import { App } from "./App";
import { jsonResponse, renderWithRouter } from "./test/test-utils";

const company = {
  id: "f4be8555-84ab-45ac-b44b-d2b355dd3a8e",
  name: "FPT Software",
  website: "https://fptsoftware.com",
  country: "Vietnam",
  createdAt: "2026-09-08T00:00:00Z",
  updatedAt: "2026-09-08T00:00:00Z",
};

let fetchMock: ReturnType<typeof vi.fn>;

beforeEach(() => {
  fetchMock = vi.fn();
  vi.stubGlobal("fetch", fetchMock);
});

it("takes the research-style home entry to a prefilled company form", async () => {
  fetchMock.mockResolvedValueOnce(jsonResponse([]));
  const user = userEvent.setup();
  renderWithRouter(<App />);

  await user.type(screen.getByLabelText(/company name/i), "FPT Software");
  await user.click(screen.getByRole("button", { name: "Continue" }));

  expect(await screen.findByRole("heading", { name: "Company details" })).toBeInTheDocument();
  expect(screen.getByLabelText(/company name/i)).toHaveValue("FPT Software");
});

it("validates the company form before submitting", async () => {
  const user = userEvent.setup();
  renderWithRouter(<App />, "/companies/new");

  await user.click(screen.getByRole("button", { name: "Create company" }));

  expect(screen.getByText("Company name is required.")).toBeInTheDocument();
  expect(fetchMock).not.toHaveBeenCalled();
});

it("creates a company and opens the returned company profile", async () => {
  fetchMock
    .mockResolvedValueOnce(jsonResponse(company))
    .mockResolvedValueOnce(jsonResponse(company));
  const user = userEvent.setup();
  renderWithRouter(<App />, "/companies/new?name=FPT%20Software");

  await user.type(screen.getByLabelText(/^website/i), "https://fptsoftware.com");
  await user.type(screen.getByLabelText(/^country/i), "Vietnam");
  await user.click(screen.getByRole("button", { name: "Create company" }));

  expect(await screen.findByRole("heading", { name: "FPT Software" })).toBeInTheDocument();
  expect(fetchMock).toHaveBeenNthCalledWith(1, "/api/companies", expect.objectContaining({ method: "POST" }));
  expect(fetchMock).toHaveBeenNthCalledWith(2, `/api/companies/${company.id}`, expect.anything());
});

it("renders stored companies as profile links", async () => {
  fetchMock.mockResolvedValueOnce(jsonResponse([company]));
  renderWithRouter(<App />, "/companies");

  const companyLink = await screen.findByRole("link", { name: "FPT Software" });
  expect(companyLink).toHaveAttribute("href", `/companies/${company.id}`);
  expect(screen.getByRole("link", { name: "fptsoftware.com" })).toHaveAttribute("href", "https://fptsoftware.com");
});

it("shows a useful API-unavailable state for the company directory", async () => {
  fetchMock.mockRejectedValueOnce(new TypeError("network unavailable"));
  renderWithRouter(<App />, "/companies");

  expect(await screen.findByText(/couldn't reach the server/i)).toBeInTheDocument();
});

it("renders the Day 1 profile shell without pretending research is available", async () => {
  fetchMock.mockResolvedValueOnce(jsonResponse(company));
  renderWithRouter(<App />, `/companies/${company.id}`);

  expect(await screen.findByText("Research has not started yet.")).toBeInTheDocument();
  expect(screen.getByRole("tab", { name: /Sources Later/i })).toBeDisabled();
});

it("switches the theme and persists the explicit preference across a remount", async () => {
  const user = userEvent.setup();
  const view = renderWithRouter(<App />, "/settings");

  await user.click(screen.getByRole("radio", { name: /Dark/i }));

  await waitFor(() => expect(document.documentElement.dataset.theme).toBe("dark"));
  expect(localStorage.getItem("raven-theme-preference")).toBe("dark");

  view.unmount();
  renderWithRouter(<App />, "/settings");
  expect(screen.getByRole("radio", { name: /Dark/i })).toBeChecked();
  expect(document.documentElement.dataset.theme).toBe("dark");
});

it("lets System preference follow the current OS theme", async () => {
  localStorage.setItem("raven-theme-preference", "light");
  const user = userEvent.setup();
  renderWithRouter(<App />, "/settings");

  expect(document.documentElement.dataset.theme).toBe("light");
  await user.click(screen.getByRole("radio", { name: /System/i }));

  expect(localStorage.getItem("raven-theme-preference")).toBe("system");
  expect(document.documentElement.dataset.theme).toBe("light");
});
