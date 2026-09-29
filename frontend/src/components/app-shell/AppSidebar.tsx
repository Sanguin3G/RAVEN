import { Fragment, type MouseEvent, type ReactNode, type RefObject, type ComponentType } from "react";
import { Link } from "react-router-dom";
import { Buildings, CaretLeft, CaretRight, GearSix, MagnifyingGlass, Pulse, SquaresFour, X, type IconProps } from "@phosphor-icons/react";
import { CompanyReturnLink } from "../CompanyReturnLink";
import type { WorkspaceServiceState } from "../../hooks/useWorkspaceServiceHealth";

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

type Props = {
  isSidebarCollapsed: boolean;
  setSidebarCollapsed: (update: (collapsed: boolean) => boolean) => void;
  mobileCloseButtonRef: RefObject<HTMLButtonElement | null>;
  closeMobileNavigation: (returnFocus?: boolean) => void;
  pathname: string;
  navigateGlobal: (event: MouseEvent<HTMLAnchorElement>, destination: string) => void;
  showCompanyReturn: boolean;
  workspaceServiceState: WorkspaceServiceState;
  workspaceServiceLabel: string;
  accountMenu: ReactNode;
};

export function AppSidebar({ isSidebarCollapsed, setSidebarCollapsed, mobileCloseButtonRef, closeMobileNavigation, pathname, navigateGlobal, showCompanyReturn, workspaceServiceState, workspaceServiceLabel, accountMenu }: Props) {
  return (
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
            return <Fragment key={item.to}><Link
              aria-current={isNavigationItemActive(item, pathname) ? "page" : undefined}
              className={linkClass(isNavigationItemActive(item, pathname))}
              title={item.label}
              to={item.to}
              state={null}
              onClick={(event) => navigateGlobal(event, item.to)}
            >
              <ItemIcon size={19} weight="bold" />
              <span className="sidebar-nav__label">{item.label}</span>
            </Link>
            {item.to === "/companies" && showCompanyReturn ? <CompanyReturnLink onNavigate={() => closeMobileNavigation()} /> : null}
            </Fragment>
          })}
        </nav>

        <div className={`app-sidebar__service-state app-sidebar__service-state--${workspaceServiceState}`}>
          <span className="app-sidebar__footer-mark" aria-hidden="true" />
          <span>Research services {workspaceServiceLabel.toLowerCase()}</span>
        </div>

        {accountMenu}
      </aside>
  );
}
