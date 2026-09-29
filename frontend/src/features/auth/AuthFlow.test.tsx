import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes, useLocation } from "react-router-dom";
import { expect, it, vi } from "vitest";
import { jsonResponse, renderWithRouter } from "../../test/test-utils";
import { AuthProvider } from "./AuthProvider";
import { LoginPage } from "./LoginPage";
import { RequireAuth } from "./RequireAuth";

function AuthTestRoutes() {
  const location = useLocation();
  return <>
    <output data-testid="current-location">{`${location.pathname}${location.search}${location.hash}`}</output>
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="*" element={<RequireAuth><h1>Private workspace</h1></RequireAuth>} />
    </Routes>
  </>;
}

it("redirects an anonymous user to login and returns to the requested page", async () => {
  const user = userEvent.setup();
  const fetchMock = vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
    const url = String(input);
    if (url.endsWith("/api/auth/me")) return jsonResponse({ title: "Unauthorized", status: 401 }, 401);
    if (url.endsWith("/api/auth/login") && init?.method === "POST") return jsonResponse({
      id: "researcher-id",
      displayName: "RAVEN Researcher",
      email: "researcher@example.invalid",
      roles: ["Researcher"],
    });
    return jsonResponse({}, 404);
  });

  renderWithRouter(<AuthProvider><AuthTestRoutes /></AuthProvider>, "/companies/alpha?tab=sources");

  expect(await screen.findByRole("heading", { name: "Sign in to RAVEN" })).toBeInTheDocument();
  await user.type(screen.getByLabelText("Email"), "researcher@example.invalid");
  await user.type(screen.getByLabelText("Password"), "example-password");
  await user.click(screen.getByRole("button", { name: "Sign in" }));

  expect(await screen.findByRole("heading", { name: "Private workspace" })).toBeInTheDocument();
  expect(screen.getByTestId("current-location")).toHaveTextContent("/companies/alpha?tab=sources");
  expect(fetchMock).toHaveBeenCalledWith("/api/auth/login", expect.objectContaining({ method: "POST", credentials: "include" }));
});
