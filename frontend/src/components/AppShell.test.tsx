import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { it, expect, vi } from "vitest";
import { useLocation } from "react-router-dom";
import { AppShell } from "./AppShell";
import { renderWithRouter } from "../test/test-utils";

vi.mock("../features/auth/AuthProvider", () => ({
  useAuth: () => ({ status: "authenticated", user: { id: "test-admin", displayName: "Test Admin", email: "admin@example.invalid", roles: ["Admin"] }, isAdmin: true, error: null, refresh: vi.fn(), login: vi.fn(), logout: vi.fn() }),
}));

vi.mock("../api/companies", () => ({
  getCompany: vi.fn(async (id: string) => ({ id, name: "70mai" })),
}));

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location-probe">{`${location.pathname}${location.search}${location.hash}`}</output>;
}

it("marks only the exact research route as active", () => {
  renderWithRouter(<AppShell><p>Research workspace</p></AppShell>, "/companies/new");

  expect(screen.getByRole("link", { name: "Research Company" })).toHaveClass("active");
  expect(screen.getByRole("link", { name: "Companies" })).not.toHaveClass("active");
});

it("persists the desktop sidebar collapse preference", async () => {
  const user = userEvent.setup();
  const view = renderWithRouter(<AppShell><p>Workspace</p></AppShell>);

  await user.click(screen.getByRole("button", { name: "Collapse navigation" }));
  expect(document.querySelector(".app-shell")).toHaveAttribute("data-sidebar-collapsed", "true");
  expect(localStorage.getItem("raven-sidebar-collapsed")).toBe("true");

  view.unmount();
  renderWithRouter(<AppShell><p>Workspace</p></AppShell>);
  expect(screen.getByRole("button", { name: "Expand navigation" })).toBeInTheDocument();
  expect(document.querySelector('img[src="/raven-logo.svg"]')).toBeInTheDocument();
});

it("opens a compact account menu with appearance controls and support links", async () => {
  const user = userEvent.setup();
  renderWithRouter(<AppShell><p>Workspace</p></AppShell>);

  const trigger = screen.getByRole("button", { name: "Open account and appearance menu" });
  await user.click(trigger);

  const panel = document.getElementById("account-menu-panel");
  expect(panel).not.toBeNull();
  expect(within(panel!).getByRole("link", { name: "Settings" })).toBeInTheDocument();
  expect(within(panel!).getByRole("link", { name: "Help" })).toBeInTheDocument();
  expect(within(panel!).getByRole("link", { name: "About RAVEN" })).toBeInTheDocument();
  expect(within(panel!).getByRole("button", { name: "System" })).toHaveAttribute("aria-pressed", "true");

  await user.keyboard("{Escape}");
  expect(trigger).toHaveFocus();
});

it("dismisses the mobile navigation with Escape", async () => {
  const user = userEvent.setup();
  renderWithRouter(<AppShell><p>Workspace</p></AppShell>);

  await user.click(screen.getByRole("button", { name: "Open navigation" }));
  expect(document.querySelector(".app-shell")).toHaveAttribute("data-mobile-open", "true");

  fireEvent.keyDown(document, { key: "Escape" });
  expect(document.querySelector(".app-shell")).toHaveAttribute("data-mobile-open", "false");
});

it("returns from Settings to the full company route while Companies always opens the list", async () => {
  const user = userEvent.setup();
  sessionStorage.clear();
  renderWithRouter(<><AppShell><p>Workspace</p></AppShell><LocationProbe /></>, "/companies/company-a?tab=briefings&conversation=chat-a");

  await user.click(screen.getByRole("link", { name: "Settings" }));
  expect(await screen.findByRole("link", { name: "Return to 70mai" })).toBeInTheDocument();
  expect(sessionStorage.getItem("raven:return-company-route")).toContain("company-a?tab=briefings&conversation=chat-a");
  await user.click(screen.getByRole("button", { name: "Open account and appearance menu" }));
  await user.click(screen.getByRole("link", { name: "Help" }));
  expect(await screen.findByRole("link", { name: "Return to 70mai" })).toBeInTheDocument();
  await user.click(screen.getByRole("link", { name: "Dashboard" }));
  expect(await screen.findByRole("link", { name: "Return to 70mai" })).toBeInTheDocument();
  await user.click(screen.getByRole("link", { name: "Return to 70mai" }));
  expect(screen.getByTestId("location-probe")).toHaveTextContent("/companies/company-a?tab=briefings&conversation=chat-a");

  await user.click(screen.getByRole("link", { name: "Settings" }));
  await user.click(screen.getByRole("link", { name: "Companies" }));
  await waitFor(() => expect(screen.getByTestId("location-probe")).toHaveTextContent("/companies"));
  expect(sessionStorage.getItem("raven:return-company-route")).toBeNull();
});

it("does not invent a company return action when Settings is opened from the list", async () => {
  const user = userEvent.setup();
  sessionStorage.clear();
  renderWithRouter(<><AppShell><p>Workspace</p></AppShell><LocationProbe /></>, "/companies");

  await user.click(screen.getByRole("link", { name: "Settings" }));
  await waitFor(() => expect(screen.getByTestId("location-probe")).toHaveTextContent("/settings"));
  expect(screen.queryByRole("link", { name: /Return to/ })).not.toBeInTheDocument();
  expect(sessionStorage.getItem("raven:return-company-route")).toBeNull();
});
