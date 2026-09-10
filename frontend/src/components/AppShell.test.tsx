import { fireEvent, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { it, expect } from "vitest";
import { AppShell } from "./AppShell";
import { renderWithRouter } from "../test/test-utils";

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

it("opens a compact account menu with appearance controls and future placeholders", async () => {
  const user = userEvent.setup();
  renderWithRouter(<AppShell><p>Workspace</p></AppShell>);

  const trigger = screen.getByRole("button", { name: "Open account and appearance menu" });
  await user.click(trigger);

  const panel = document.getElementById("account-menu-panel");
  expect(panel).not.toBeNull();
  expect(within(panel!).getByRole("link", { name: "Settings" })).toBeInTheDocument();
  expect(within(panel!).getByRole("button", { name: /help coming soon/i })).toBeDisabled();
  expect(within(panel!).getByRole("button", { name: /about raven coming soon/i })).toBeDisabled();
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
