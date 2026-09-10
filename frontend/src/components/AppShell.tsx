import { useEffect, useRef, useState, type ComponentType, type ReactNode } from "react";
import { Link, useLocation } from "react-router-dom";
import {
  Buildings,
  CaretLeft,
  CaretRight,
  Desktop,
  GearSix,
  Info,
  List,
  MagnifyingGlass,
  Moon,
  Palette,
  Pulse,
  Question,
  SquaresFour,
  Sun,
  UserCircle,
  X,
  type IconProps,
} from "@phosphor-icons/react";
import { useTheme, type ThemePreference } from "../app/theme";

const sidebarStorageKey = "raven-sidebar-collapsed";

type NavigationItem = {
  label: string;
  to: string;
  icon: ComponentType<IconProps>;
  exact?: boolean;
};

const navigationItems: NavigationItem[] = [
  { label: "Dashboard", to: "/", icon: SquaresFour, exact: true },
  { label: "Companies", to: "/companies", icon: Buildings },
  { label: "Research Company", to: "/companies/new", icon: MagnifyingGlass, exact: true },
  { label: "System status", to: "/status", icon: Pulse, exact: true },
  { label: "Settings", to: "/settings", icon: GearSix, exact: true },
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
  if (pathname === "/status") return "System status";
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
  const { preference, resolvedTheme, setPreference } = useTheme();
  const [isSidebarCollapsed, setSidebarCollapsed] = useState(readSidebarPreference);
  const [isMobileOpen, setMobileOpen] = useState(false);
  const [isAccountMenuOpen, setAccountMenuOpen] = useState(false);
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

  const quickThemes: Array<{ value: ThemePreference; label: string; icon: ComponentType<IconProps> }> = [
    { value: "system", label: "System", icon: Desktop },
    { value: "light", label: "Light", icon: Sun },
    { value: "dark", label: "Dark", icon: Moon },
  ];

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
            <span className="app-sidebar__logo-frame" aria-hidden="true"><img src="/raven-logo.svg" alt="" /></span>
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
            <span>{isSidebarCollapsed ? "Expand" : "Collapse"}</span>
            {isSidebarCollapsed ? <CaretRight size={18} weight="bold" /> : <CaretLeft size={18} weight="bold" />}
          </button>
          <button
            ref={mobileCloseButtonRef}
            className="mobile-sidebar-close"
            type="button"
            aria-label="Close navigation"
            onClick={() => closeMobileNavigation(true)}
          >
            <X size={20} weight="bold" />
          </button>
        </div>

        <nav className="sidebar-nav" aria-label="Primary navigation">
          <span className="sidebar-nav__heading">Workspace</span>
          {navigationItems.map((item) => {
            const ItemIcon = item.icon;
            return <Link
              key={item.to}
              aria-current={isNavigationItemActive(item, location.pathname) ? "page" : undefined}
              className={linkClass(isNavigationItemActive(item, location.pathname))}
              title={item.label}
              to={item.to}
              onClick={() => closeMobileNavigation()}
            >
              <ItemIcon size={19} weight="bold" />
              <span className="sidebar-nav__label">{item.label}</span>
            </Link>
          })}
        </nav>

        <div className="app-sidebar__service-state">
          <span className="app-sidebar__footer-mark" aria-hidden="true" />
          <span>Research services operational</span>
        </div>

        <div className="account-menu" ref={accountMenuRef}>
          {isAccountMenuOpen && (
            <div className="account-menu__panel" id="account-menu-panel">
              <div className="account-menu__heading">
                <span className="account-menu__avatar" aria-hidden="true"><UserCircle size={23} weight="fill" /></span>
                <span><strong>Local workspace</strong><small>Authentication will live here.</small></span>
              </div>
              <div className="account-menu__section">
                <span className="account-menu__label"><Palette size={16} weight="fill" /> Appearance</span>
                <div className="account-menu__themes" aria-label="Quick appearance choices">
                  {quickThemes.map((theme) => {
                    const ThemeIcon = theme.icon;
                    return <button
                      aria-pressed={preference === theme.value}
                      className={preference === theme.value ? "active" : undefined}
                      key={theme.value}
                      onClick={() => setPreference(theme.value)}
                      type="button"
                    >
                      <ThemeIcon size={16} weight={preference === theme.value ? "fill" : "regular"} /> {theme.label}
                    </button>
                  })}
                </div>
                <small className="account-menu__theme-note">Using {preference === "system" ? `system ${resolvedTheme}` : preference} appearance.</small>
              </div>
              <div className="account-menu__links">
                <Link to="/settings"><GearSix size={17} weight="duotone" /> Settings</Link>
                <button type="button" disabled><Question size={17} weight="duotone" /> Help <small>Coming soon</small></button>
                <button type="button" disabled><Info size={17} weight="duotone" /> About RAVEN <small>Coming soon</small></button>
              </div>
            </div>
          )}
          <button
            aria-controls="account-menu-panel"
            aria-expanded={isAccountMenuOpen}
            aria-label={isAccountMenuOpen ? "Close account and appearance menu" : "Open account and appearance menu"}
            className="account-menu__trigger"
            onClick={() => setAccountMenuOpen((open) => !open)}
            ref={accountMenuButtonRef}
            type="button"
          >
            <span className="account-menu__avatar" aria-hidden="true"><UserCircle size={25} weight="fill" /></span>
            <span className="account-menu__trigger-copy"><strong>Local workspace</strong><small>Account & appearance</small></span>
          </button>
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
            <List size={23} weight="bold" />
          </button>
          <div className="context-topbar__context">
            <span className="context-topbar__eyebrow">RAVEN workspace</span>
            <strong>{routeTitle(location.pathname)}</strong>
          </div>
          <Link className="context-topbar__status" to="/status" aria-label="View operational status">
            <span className="status-indicator" aria-hidden="true" />
            <span>Operational</span>
          </Link>
        </header>

        <main className="page-content" id="main-content">{children}</main>
        <footer className="site-footer">RAVEN is building a public-source company record.</footer>
      </div>
    </div>
  );
}
