import type { MouseEvent, RefObject } from "react";
import { Link } from "react-router-dom";
import { List } from "@phosphor-icons/react";
import type { WorkspaceServiceState } from "../../hooks/useWorkspaceServiceHealth";

type Props = {
  mobileMenuButtonRef: RefObject<HTMLButtonElement | null>;
  isMobileOpen: boolean;
  setMobileOpen: (open: boolean) => void;
  pathname: string;
  workspaceServiceState: WorkspaceServiceState;
  workspaceServiceLabel: string;
  navigateGlobal: (event: MouseEvent<HTMLAnchorElement>, destination: string) => void;
};

function routeTitle(pathname: string) {
  if (pathname === "/") return "Dashboard";
  if (pathname === "/companies") return "Companies";
  if (pathname === "/companies/new") return "Research Company";
  if (pathname.startsWith("/companies/")) return "Company workspace";
  if (pathname === "/settings") return "Settings";
  if (pathname === "/status") return "System status";
  if (pathname === "/help") return "Help";
  if (pathname === "/about") return "About RAVEN";
  return "RAVEN workspace";
}

export function ContextTopbar({ mobileMenuButtonRef, isMobileOpen, setMobileOpen, pathname, workspaceServiceState, workspaceServiceLabel, navigateGlobal }: Props) {
  return (
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
            <strong>{routeTitle(pathname)}</strong>
          </div>
          <Link className={`context-topbar__status context-topbar__status--${workspaceServiceState}`} to="/status" state={null} onClick={(event) => navigateGlobal(event, "/status")} aria-label="View operational status">
            <span className="status-indicator" aria-hidden="true" />
            <span>{workspaceServiceLabel}</span>
          </Link>
        </header>
  );
}
