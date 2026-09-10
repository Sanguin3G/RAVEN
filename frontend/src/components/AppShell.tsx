import { useEffect, useRef, useState, type ReactNode } from "react";
import { Link, useLocation } from "react-router-dom";
import { Icon, type IconName } from "./Icon";

const sidebarStorageKey = "raven-sidebar-collapsed";

type NavigationItem = {
  label: string;
  to: string;
  icon: IconName;
  exact?: boolean;
};

const navigationItems: NavigationItem[] = [
  { label: "Dashboard", to: "/", icon: "dashboard", exact: true },
  { label: "Companies", to: "/companies", icon: "buildings" },
  { label: "Research Company", to: "/companies/new", icon: "magnifying-glass", exact: true },
  { label: "Settings", to: "/settings", icon: "gear", exact: true },
];

function readSidebarPreference() {
  if (typeof window === "undefined") return false;

  try {
    return window.localStorage.getItem(sidebarStorageKey) === "true";
  } catch {
    return false;
  }
}

function routeTitle(pathname: string) {
  if (pathname === "/") return "Dashboard";
  if (pathname === "/companies") return "Companies";
  if (pathname === "/companies/new") return "Research Company";
  if (pathname.startsWith("/companies/")) return "Company workspace";
  if (pathname === "/settings") return "Settings";
  return "RAVEN workspace";
}

function isCompaniesRoute(pathname: string) {
  return pathname === "/companies" || (pathname.startsWith("/companies/") && pathname !== "/companies/new");
}

function linkClass(isActive: boolean) {
  return `sidebar-nav__link${isActive ? " active" : ""}`;
}

function isNavigationItemActive(item: NavigationItem, pathname: string) {
  if (item.to === "/") return pathname === "/";
  if (item.to === "/companies") return isCompaniesRoute(pathname);
  return item.exact ? pathname === item.to : pathname.startsWith(item.to);
}

export function AppShell({ children }: { children: ReactNode }) {
  const location = useLocation();
  const [isSidebarCollapsed, setSidebarCollapsed] = useState(readSidebarPreference);
  const [isMobileOpen, setMobileOpen] = useState(false);
  const mobileMenuButtonRef = useRef<HTMLButtonElement>(null);
  const mobileCloseButtonRef = useRef<HTMLButtonElement>(null);

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
  }, [location.pathname]);

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

  return (
    <div
      className="app-shell"
      data-mobile-open={isMobileOpen}
      data-sidebar-collapsed={isSidebarCollapsed}
    >
      <aside id="application-navigation" className="app-sidebar" aria-label="Application navigation">
        <div className="app-sidebar__header">
          <Link className="app-sidebar__brand" to="/" aria-label="RAVEN home" onClick={() => closeMobileNavigation()}>
            <span className="brand__mark" aria-hidden="true">R</span>
            <span className="app-sidebar__wordmark">
              <strong>RAVEN</strong>
              <small>Company intelligence</small>
            </span>
          </Link>
          <button
            className="sidebar-toggle"
            type="button"
            aria-controls="application-navigation"
            aria-label={isSidebarCollapsed ? "Expand navigation" : "Collapse navigation"}
            aria-expanded={!isSidebarCollapsed}
            onClick={() => setSidebarCollapsed((collapsed) => !collapsed)}
          >
            <Icon name={isSidebarCollapsed ? "chevron-right" : "chevron-left"} />
          </button>
          <button
            ref={mobileCloseButtonRef}
            className="mobile-sidebar-close"
            type="button"
            aria-label="Close navigation"
            onClick={() => closeMobileNavigation(true)}
          >
            <Icon name="x" />
          </button>
        </div>

        <nav className="sidebar-nav" aria-label="Primary navigation">
          <span className="sidebar-nav__heading">Workspace</span>
          {navigationItems.map((item) => (
            <Link
              key={item.to}
              aria-current={isNavigationItemActive(item, location.pathname) ? "page" : undefined}
              className={linkClass(isNavigationItemActive(item, location.pathname))}
              title={item.label}
              to={item.to}
              onClick={() => closeMobileNavigation()}
            >
              <Icon name={item.icon} />
              <span className="sidebar-nav__label">{item.label}</span>
            </Link>
          ))}
        </nav>

        <div className="app-sidebar__footer">
          <span className="app-sidebar__footer-mark" aria-hidden="true" />
          <span>Public-source research</span>
        </div>
      </aside>

      <button
        className="app-shell__backdrop"
        type="button"
        aria-label="Close navigation"
        onClick={() => closeMobileNavigation(true)}
      />

      <div className="app-shell__main">
        <header className="context-topbar">
          <button
            ref={mobileMenuButtonRef}
            className="mobile-menu-button"
            type="button"
            aria-controls="application-navigation"
            aria-expanded={isMobileOpen}
            aria-label="Open navigation"
            onClick={() => setMobileOpen(true)}
          >
            <Icon name="menu" />
          </button>
          <div className="context-topbar__context">
            <span className="context-topbar__eyebrow">RAVEN workspace</span>
            <strong>{routeTitle(location.pathname)}</strong>
          </div>
          <div className="context-topbar__status" aria-label="Application status">
            <span className="status-indicator" aria-hidden="true" />
            <span>Local workspace</span>
          </div>
        </header>

        <main className="page-content" id="main-content">{children}</main>
        <footer className="site-footer">RAVEN is building a public-source company record.</footer>
      </div>
    </div>
  );
}
