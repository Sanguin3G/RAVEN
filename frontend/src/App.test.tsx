import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, expect, it } from "vitest";
import { App } from "./App";
import { renderWithRouter } from "./test/test-utils";

beforeEach(() => {
  localStorage.clear();
});

it("renders the Day 2 dashboard and new navigation", () => {
  renderWithRouter(<App />);

  expect(screen.getByRole("heading", { name: "Company intelligence, at a glance." })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Dashboard" })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Company List" })).toBeInTheDocument();
  expect(screen.getByRole("link", { name: "Add Company Profile" })).toBeInTheDocument();
  expect(screen.queryByText("Ask RAVEN")).not.toBeInTheDocument();
});

it("filters the company list by name", async () => {
  const user = userEvent.setup();
  renderWithRouter(<App />, "/companies");

  expect(screen.getByRole("columnheader", { name: "Logo" })).toBeInTheDocument();
  expect(screen.getByRole("columnheader", { name: "Last update" })).toBeInTheDocument();
  await user.type(screen.getByPlaceholderText("Search by company name"), "Masan");

  expect(screen.getByRole("link", { name: "Masan Group" })).toBeInTheDocument();
  expect(screen.queryByRole("link", { name: "FPT Software" })).not.toBeInTheDocument();
});

it("opens a company detail from the list", async () => {
  const user = userEvent.setup();
  renderWithRouter(<App />, "/companies");
  await user.click(screen.getByRole("link", { name: "FPT Software" }));

  expect(screen.getByRole("heading", { name: "FPT Software" })).toBeInTheDocument();
  expect(screen.getByText("Global technology services company delivering digital transformation and software solutions.")).toBeInTheDocument();
});

it("searches and opens the company match popup", async () => {
  const user = userEvent.setup();
  renderWithRouter(<App />, "/companies/new");
  await user.type(screen.getByLabelText(/Company name/), "FPT");
  await user.click(screen.getByRole("button", { name: "Search matching companies" }));
  await user.click(screen.getByRole("button", { name: /FPT Software/ }));

  expect(screen.getByRole("dialog")).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Auto Generate Profile" })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "Manually Create Profile" })).toBeInTheDocument();
});

it("supports manual profile creation for an unmatched company", async () => {
  const user = userEvent.setup();
  renderWithRouter(<App />, "/companies/new");
  await user.type(screen.getByLabelText(/Company name/), "Unknown Atlas Co");
  await user.click(screen.getByRole("button", { name: "Search matching companies" }));
  await user.click(screen.getByRole("button", { name: "Review entered company" }));
  await user.click(screen.getByRole("button", { name: "Manually Create Profile" }));

  expect(screen.getByRole("heading", { name: "Unknown Atlas Co" })).toBeInTheDocument();
  expect(screen.getByText(/Manual profile mode/)).toBeInTheDocument();
});

it("switches and persists the selected theme", async () => {
  const user = userEvent.setup();
  const view = renderWithRouter(<App />, "/settings");
  await user.click(screen.getByRole("radio", { name: /Dark/i }));

  expect(document.documentElement.dataset.theme).toBe("dark");
  expect(localStorage.getItem("raven-theme-preference")).toBe("dark");
  view.unmount();
  renderWithRouter(<App />, "/settings");
  expect(screen.getByRole("radio", { name: /Dark/i })).toBeChecked();
});
