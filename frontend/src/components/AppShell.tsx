import { useEffect, useRef, useState, type MouseEvent, type ReactNode } from "react";
import { Outlet, useLocation, useNavigate } from "react-router-dom";
import { useTheme } from "../app/theme";
import { useAuth } from "../features/auth/AuthProvider";
import { useWorkspaceServiceHealth } from "../hooks/useWorkspaceServiceHealth";
import { useActiveResearchRuns } from "../hooks/useActiveResearchRuns";
import { useSharedResearchActivity } from "../hooks/useSharedResearchActivity";
import { clearCompanyReturnRoute, readCompanyReturnRoute, rememberCompanyReturnRoute } from "../utils/companyReturnRoute";
import { AccountMenu } from "./app-shell/AccountMenu";
import { ContextTopbar } from "./app-shell/ContextTopbar";
import { AppSidebar } from "./app-shell/AppSidebar";
import { ResearchActivityPanel } from "./app-shell/ResearchActivityPanel";

const sidebarStorageKey = "raven-sidebar-collapsed";


function readSidebarPreference() {
  if (typeof window === "undefined") return false;

  try {
    return window.localStorage.getItem(sidebarStorageKey) === "true";
  } catch {
    return false;
  }
}

function supportsCompanyReturn(pathname: string) {
  return pathname === "/" || pathname === "/companies/new" || pathname === "/settings" ||
    pathname === "/status" || pathname === "/help" || pathname === "/about";
}



export function AppShell({ children }: { children?: ReactNode }) {
  const location = useLocation();
  const navigate = useNavigate();
  const { user, logout } = useAuth();
  const { preference, resolvedTheme, setPreference } = useTheme();
  const [isSidebarCollapsed, setSidebarCollapsed] = useState(readSidebarPreference);
  const [isMobileOpen, setMobileOpen] = useState(false);
  const [isAccountMenuOpen, setAccountMenuOpen] = useState(false);
  const workspaceServiceState = useWorkspaceServiceHealth();
  const { activeResearch, currentResearchSession, cancelActiveResearch, togglePauseResearch } = useActiveResearchRuns();
  const sharedResearchActivities = useSharedResearchActivity();

  const navigateGlobal = (event: MouseEvent<HTMLAnchorElement>, destination: string) => {
    if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    const isContextualDestination = supportsCompanyReturn(destination);
    const hasCompanyReturnState = !!(location.state && typeof location.state === "object" &&
      "companyReturn" in location.state && location.state.companyReturn === true);
    const context = isContextualDestination
      ? rememberCompanyReturnRoute(location.pathname, location.search, location.hash) ??
        (hasCompanyReturnState ? readCompanyReturnRoute() : null)
      : null;
    if ((isContextualDestination && !context) || !isContextualDestination) clearCompanyReturnRoute();
    navigate(destination, { state: context ? { companyReturn: true } : null });
    closeMobileNavigation();
    setAccountMenuOpen(false);
  };
  const handleLogout = () => {
    void logout().catch(() => undefined).finally(() => navigate("/login", { replace: true }));
  };
  const hasCompanyReturnState = !!(location.state && typeof location.state === "object" &&
    "companyReturn" in location.state && location.state.companyReturn === true);
  const showCompanyReturn = supportsCompanyReturn(location.pathname) && hasCompanyReturnState;

  useEffect(() => {
    if (supportsCompanyReturn(location.pathname) && !hasCompanyReturnState) clearCompanyReturnRoute();
  }, [hasCompanyReturnState, location.pathname]);
  const mobileMenuButtonRef = useRef<HTMLButtonElement>(null);
  const mobileCloseButtonRef = useRef<HTMLButtonElement>(null);
  const accountMenuRef = useRef<HTMLDivElement>(null);
  const accountMenuButtonRef = useRef<HTMLButtonElement>(null);


  const closeMobileNavigation = (returnFocus = false) => {
    setMobileOpen(false);
    if (returnFocus) mobileMenuButtonRef.current?.focus();
  };

  useEffect(() => {
    try {
      window.localStorage.setItem(sidebarStorageKey, String(isSidebarCollapsed));
    } catch {
      // A blocked storage context should not make navigation unusable.
    }
  }, [isSidebarCollapsed]);

  useEffect(() => {
    setMobileOpen(false);
    setAccountMenuOpen(false);
  }, [location.pathname]);

  useEffect(() => {
    if (!isAccountMenuOpen) return;

    const closeOnOutsidePointer = (event: PointerEvent) => {
      if (!accountMenuRef.current?.contains(event.target as Node)) setAccountMenuOpen(false);
    };
    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setAccountMenuOpen(false);
        accountMenuButtonRef.current?.focus();
      }
    };
    document.addEventListener("pointerdown", closeOnOutsidePointer);
    document.addEventListener("keydown", closeOnEscape);
    return () => {
      document.removeEventListener("pointerdown", closeOnOutsidePointer);
      document.removeEventListener("keydown", closeOnEscape);
    };
  }, [isAccountMenuOpen]);


  useEffect(() => {
    if (!isMobileOpen) return;

    mobileCloseButtonRef.current?.focus();

    const closeOnEscape = (event: KeyboardEvent) => {
      if (event.key === "Escape") closeMobileNavigation(true);
    };

    document.addEventListener("keydown", closeOnEscape);
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";

    return () => {
      document.removeEventListener("keydown", closeOnEscape);
      document.body.style.overflow = previousOverflow;
    };
  }, [isMobileOpen]);

  const workspaceServiceLabel = workspaceServiceState === "operational"
    ? "Operational"
    : workspaceServiceState === "unavailable"
      ? "Unavailable"
      : workspaceServiceState === "not-configured"
        ? "Not configured"
        : workspaceServiceState === "attention"
          ? "Needs attention"
          : "Checking";

  return (
    <div
      className="app-shell"
      data-mobile-open={isMobileOpen}
      data-sidebar-collapsed={isSidebarCollapsed}
    >
      <AppSidebar isSidebarCollapsed={isSidebarCollapsed} setSidebarCollapsed={setSidebarCollapsed} mobileCloseButtonRef={mobileCloseButtonRef} closeMobileNavigation={closeMobileNavigation} pathname={location.pathname} navigateGlobal={navigateGlobal} showCompanyReturn={showCompanyReturn} workspaceServiceState={workspaceServiceState} workspaceServiceLabel={workspaceServiceLabel} accountMenu={user ? <AccountMenu accountMenuRef={accountMenuRef} accountMenuButtonRef={accountMenuButtonRef} isAccountMenuOpen={isAccountMenuOpen} setAccountMenuOpen={setAccountMenuOpen} preference={preference} resolvedTheme={resolvedTheme} setPreference={setPreference} navigateGlobal={navigateGlobal} user={user} onLogout={handleLogout} /> : null} />

      <button
        className="app-shell__backdrop"
        type="button"
        aria-label="Close navigation"
        onClick={() => closeMobileNavigation(true)}
      />

      <div className="app-shell__main">
        <ContextTopbar mobileMenuButtonRef={mobileMenuButtonRef} isMobileOpen={isMobileOpen} setMobileOpen={setMobileOpen} pathname={location.pathname} workspaceServiceState={workspaceServiceState} workspaceServiceLabel={workspaceServiceLabel} navigateGlobal={navigateGlobal} />

        <ResearchActivityPanel activeResearch={activeResearch} currentResearchSession={currentResearchSession} sharedResearchActivities={sharedResearchActivities} togglePauseResearch={togglePauseResearch} cancelActiveResearch={cancelActiveResearch} />

        <main className="page-content" id="main-content">{children ?? <Outlet />}</main>
        <footer className="site-footer">RAVEN is building a public-source company record.</footer>
      </div>
    </div>
  );
}
