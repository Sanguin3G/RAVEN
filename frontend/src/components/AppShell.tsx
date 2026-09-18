import { useEffect, useRef, useState, type ComponentType, type ReactNode } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import {
  Buildings,
  CaretLeft,
  CaretRight,
  CheckCircle,
  CircleNotch,
  Desktop,
  FileArrowUp,
  GearSix,
  Info,
  LockSimple,
  List,
  MagnifyingGlass,
  Moon,
  Palette,
  Pause,
  Play,
  Pulse,
  Question,
  SquaresFour,
  Sparkle,
  Sun,
  UserCircle,
  WarningCircle,
  X,
  type IconProps,
} from "@phosphor-icons/react";
import { useTheme, type ThemePreference } from "../app/theme";
import { cancelResearchRun, getActiveResearchRuns, getResearchRun } from "../api/research";
import { getManagedResearchJobs } from "../api/managedResearch";
import { getExternalResearchAnalysis } from "../api/externalResearch";
import { getCurrentCompanyProfile } from "../api/profiles";
import type { ActiveResearchRun } from "../types/research";
import { clearCurrentResearch, readCurrentResearch, rememberCurrentResearch, setCurrentResearchPaused, type CurrentResearchSession } from "../utils/researchSession";
import { canPauseResearchStage, isFinishedResearch, researchProgressLabel } from "../utils/researchProgress";
import { dismissResearchActivity, updateResearchActivity, useResearchActivities } from "../utils/researchActivity";
import { getApiHealth, getProviderStatus } from "../api/system";
import { hasUsableAcceptedProfile } from "../utils/profileReadiness";

const sidebarStorageKey = "raven-sidebar-collapsed";

type NavigationItem = {
  label: string;
  to: string;
  icon: ComponentType<IconProps>;
  exact?: boolean;
};

type WorkspaceServiceState = "checking" | "operational" | "attention" | "unavailable" | "not-configured";

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

function globalResearchState(item: ActiveResearchRun, session: CurrentResearchSession | null) {
  if (session?.runId === item.run.id && session.paused) return "paused";
  if (item.run.stage === "Failed") return "failed";
  if (canPauseResearchStage(item.run.stage)) return "ready";
  return "active";
}

export function AppShell({ children }: { children: ReactNode }) {
  const location = useLocation();
  const navigate = useNavigate();
  const { preference, resolvedTheme, setPreference } = useTheme();
  const [isSidebarCollapsed, setSidebarCollapsed] = useState(readSidebarPreference);
  const [isMobileOpen, setMobileOpen] = useState(false);
  const [isAccountMenuOpen, setAccountMenuOpen] = useState(false);
  const [workspaceServiceState, setWorkspaceServiceState] = useState<WorkspaceServiceState>("checking");
  const [activeResearch, setActiveResearch] = useState<ActiveResearchRun[]>([]);
  const [currentResearchSession, setCurrentResearchSession] = useState<CurrentResearchSession | null>(readCurrentResearch);
  const sharedResearchActivities = useResearchActivities();
  const sharedResearchActivitiesRef = useRef(sharedResearchActivities);
  sharedResearchActivitiesRef.current = sharedResearchActivities;
  useEffect(() => {
    let active = true;
    const checkServices = async () => {
      const [apiResult, providerResult] = await Promise.allSettled([getApiHealth(), getProviderStatus()]);
      if (!active) return;
      if (apiResult.status !== "fulfilled" || !apiResult.value.available || providerResult.status !== "fulfilled") {
        setWorkspaceServiceState("attention");
        return;
      }
      const providers = [providerResult.value.brave, providerResult.value.exa, providerResult.value.crawl4Ai, providerResult.value.gemini].filter(Boolean);
      const configuredProviders = providers.filter((provider) => provider.configured);
      if (configuredProviders.length === 0) {
        setWorkspaceServiceState("not-configured");
      } else if (configuredProviders.some((provider) => provider.available === false)) {
        setWorkspaceServiceState("unavailable");
      } else {
        setWorkspaceServiceState("operational");
      }
    };
    void checkServices();
    const intervalId = window.setInterval(() => { void checkServices(); }, 30_000);
    return () => { active = false; window.clearInterval(intervalId); };
  }, []);

  useEffect(() => {
    let active = true;
    const refreshSharedResearch = async () => {
      const tracked = sharedResearchActivitiesRef.current.filter((activity) => activity.origin === "Deep" || activity.status === "running");
      const deepCompanyIds = [...new Set(tracked.filter((activity) => activity.origin === "Deep").map((activity) => activity.companyId))];
      const externalJobs = tracked.filter((activity) => activity.origin === "External" && activity.jobId);

      await Promise.all([
        ...deepCompanyIds.map(async (companyId) => {
          const [result, profile] = await Promise.all([
            getManagedResearchJobs(companyId).catch(() => []),
            getCurrentCompanyProfile(companyId).catch(() => null),
          ]);
          const jobs = Array.isArray(result) ? result : [];
          if (!active) return;
          jobs.forEach((job) => {
            const activity = tracked.find((item) => item.id === `deep-${job.id}`);
            if (!activity) return;
            const status: "ready" | "failed" | "running" = job.status === "Completed" ? "ready" : job.status === "Failed" || job.status === "Cancelled" ? "failed" : "running";
            const locked = status === "ready" && job.purpose === "ProfileImprovement" && !hasUsableAcceptedProfile(profile);
            updateResearchActivity(activity.id, {
              detail: locked ? "Profile required · create the company profile to unlock review" : status === "ready" ? "Ready for review" : status === "failed" ? "Deep Research could not complete" : job.status === "Queued" ? "Starting investigation" : "Researching across sources",
              status,
              locked,
              updatedAt: job.completedAt || job.createdAt,
            });
          });
        }),
        ...externalJobs.map(async (activity) => {
          const job = await getExternalResearchAnalysis(activity.companyId, activity.jobId!).catch(() => null);
          if (!active || !job) return;
          const status: "ready" | "failed" | "running" = job.status === "Completed" ? "ready" : job.status === "Failed" ? "failed" : "running";
          updateResearchActivity(activity.id, {
            detail: status === "ready" ? "Ready for review" : status === "failed" ? "Analysis failed; pasted material is preserved" : "Reading imported response",
            status,
            updatedAt: job.completedAt || job.createdAt,
          });
        }),
      ]);
    };

    void refreshSharedResearch();
    const intervalId = window.setInterval(() => { void refreshSharedResearch(); }, 5_000);
    return () => { active = false; window.clearInterval(intervalId); };
  }, []);
  const mobileMenuButtonRef = useRef<HTMLButtonElement>(null);
  const mobileCloseButtonRef = useRef<HTMLButtonElement>(null);
  const accountMenuRef = useRef<HTMLDivElement>(null);
  const accountMenuButtonRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    let active = true;
    const refreshActiveResearch = async () => {
      const serverRuns = await getActiveResearchRuns().catch(() => null);
      if (!active) return;

      let session = readCurrentResearch();
      let rememberedRun: ActiveResearchRun | null = null;

      if (session) {
        const serverRun = serverRuns?.find((item) => item.run.id === session?.runId);
        const run = serverRun?.run ?? await getResearchRun(session.runId).catch(() => null);
        if (run) {
          if (isFinishedResearch(run)) {
            clearCurrentResearch(session.runId);
            session = null;
          } else {
            if (!canPauseResearchStage(run.stage) && session.paused) {
              setCurrentResearchPaused(run.id, false);
              session = readCurrentResearch();
            }
            rememberedRun = { run, companyName: session?.companyName ?? "Company research" };
          }
        }
      }

      if (!session && serverRuns && serverRuns.length > 0) {
        const first = serverRuns[0];
        rememberCurrentResearch(first.run, first.companyName);
        session = readCurrentResearch();
      }

      const merged = [...(Array.isArray(serverRuns) ? serverRuns : [])];
      if (rememberedRun && !merged.some((item) => item.run.id === rememberedRun?.run.id)) {
        merged.unshift(rememberedRun);
      }
      setCurrentResearchSession(session);
      setActiveResearch(merged);
    };
    void refreshActiveResearch();
    const timer = window.setInterval(refreshActiveResearch, 2_500);
    return () => { active = false; window.clearInterval(timer); };
  }, []);

  async function cancelActiveResearch(runId: string) {
    try {
      await cancelResearchRun(runId);
      clearCurrentResearch(runId);
      setCurrentResearchSession(readCurrentResearch());
      setActiveResearch((current) => current.filter((item) => item.run.id !== runId));
    } catch {
      // The destination page remains the source of truth if cancellation fails.
    }
  }

  function togglePauseResearch(runId: string) {
    const item = activeResearch.find((entry) => entry.run.id === runId);
    if (!item || !canPauseResearchStage(item.run.stage)) return;
    const isPaused = currentResearchSession?.runId === runId && currentResearchSession.paused;
    const next = setCurrentResearchPaused(runId, !isPaused);
    setCurrentResearchSession(next);
  }

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

  const researchActivityReady = activeResearch.filter((item) => globalResearchState(item, currentResearchSession) === "ready").length
    + sharedResearchActivities.filter((item) => item.status === "ready" && !item.locked).length;
  const researchActivityFailed = activeResearch.filter((item) => globalResearchState(item, currentResearchSession) === "failed").length
    + sharedResearchActivities.filter((item) => item.status === "failed").length;
  const researchActivityRunning = activeResearch.filter((item) => globalResearchState(item, currentResearchSession) === "active").length
    + sharedResearchActivities.filter((item) => item.status === "running").length;
  const researchActivityPaused = activeResearch.filter((item) => globalResearchState(item, currentResearchSession) === "paused").length;
  const researchActivityOverallState = researchActivityFailed > 0
    ? "attention"
    : researchActivityRunning > 0 && researchActivityReady > 0
      ? "progress"
      : researchActivityRunning > 0 || researchActivityPaused > 0
        ? "running"
        : "ready";
  const researchActivityOverallLabel = researchActivityOverallState === "attention"
    ? "Research issues"
    : researchActivityOverallState === "ready"
      ? "Ready for review"
      : researchActivityOverallState === "progress"
        ? "Research activity"
        : "Research running";
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

        <div className={`app-sidebar__service-state app-sidebar__service-state--${workspaceServiceState}`}>
          <span className="app-sidebar__footer-mark" aria-hidden="true" />
          <span>Research services {workspaceServiceLabel.toLowerCase()}</span>
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
          <Link className={`context-topbar__status context-topbar__status--${workspaceServiceState}`} to="/status" aria-label="View operational status">
            <span className="status-indicator" aria-hidden="true" />
            <span>{workspaceServiceLabel}</span>
          </Link>
        </header>

        {(activeResearch.length > 0 || sharedResearchActivities.length > 0) && <section className={`global-research-strip global-research-strip--${researchActivityOverallState}`} aria-label="Research activity">
          <div className="global-research-strip__lead">
            {sharedResearchActivities.some((item) => item.status === "failed") || activeResearch.some((item) => item.run.stage === "Failed") ? <WarningCircle size={18} weight="fill" aria-hidden="true" /> : sharedResearchActivities.some((item) => item.status === "running") || activeResearch.some((item) => !canPauseResearchStage(item.run.stage)) ? <CircleNotch className="global-research-strip__spinner" size={18} weight="bold" aria-hidden="true" /> : <CheckCircle size={18} weight="fill" aria-hidden="true" />}
            <span><strong>{researchActivityOverallLabel}</strong><small className="global-research-strip__counts"><b>{activeResearch.filter((item) => !canPauseResearchStage(item.run.stage)).length + sharedResearchActivities.filter((item) => item.status === "running").length}</b> running <i aria-hidden="true">·</i> <b>{activeResearch.filter((item) => canPauseResearchStage(item.run.stage)).length + sharedResearchActivities.filter((item) => item.status === "ready" && !item.locked).length}</b> ready <i aria-hidden="true">·</i> <b>{researchActivityFailed}</b> issue{researchActivityFailed === 1 ? "" : "s"}</small></span>
          </div>
          <div className="global-research-strip__items">
            {activeResearch.slice(0, 3).map((item) => {
              const { run, companyName } = item;
              const paused = currentResearchSession?.runId === run.id && currentResearchSession.paused;
              const state = globalResearchState(item, currentResearchSession);
              return <div className={`global-research-run global-research-run--${state}`} key={run.id}>
                <Link to={`/companies/new?researchRun=${encodeURIComponent(run.id)}`}><MagnifyingGlass size={16} weight="bold" aria-hidden="true" /><span><small className="global-research-run__origin">RAVEN Research</small><strong>{companyName}</strong><small>{researchProgressLabel(run, paused)}</small></span></Link>
                {canPauseResearchStage(run.stage) ? <button type="button" className="global-research-run__pause" onClick={() => togglePauseResearch(run.id)}>{paused ? <><Play size={13} weight="fill" aria-hidden="true" /> Resume</> : <><Pause size={13} weight="fill" aria-hidden="true" /> Pause</>}</button> : null}
                {run.stage !== "Completed" ? <button type="button" className="global-research-run__cancel" onClick={() => void cancelActiveResearch(run.id)}>Cancel</button> : null}
              </div>;
            })}
            {sharedResearchActivities.slice(0, 6).map((activity) => {
              const Icon = activity.origin === "Deep" ? Sparkle : FileArrowUp;
              const locked = activity.status === "ready" && activity.locked;
              const originLabel = activity.origin === "Deep" ? "Deep Research" : activity.origin === "Native" ? "RAVEN Research" : "External AI Assist";
              const content = <><Icon size={16} weight={activity.origin === "Deep" ? "fill" : "bold"} aria-hidden="true" /><span><small className="global-research-run__origin">{originLabel}</small><strong>{activity.companyName}</strong><small>{locked ? "Create profile to unlock review" : activity.status === "ready" ? "Ready for review" : activity.detail}</small>{activity.status === "ready" && !locked ? <em className="global-research-run__cta">Review result</em> : null}</span></>;
              const accessibleName = `${originLabel} · ${activity.companyName} · ${activity.status === "ready" ? "Review result" : activity.detail}`;
              const openActivity = () => {
                if (locked) return;
                if (activity.status === "ready") dismissResearchActivity(activity.id);
                const destinationPath = activity.href?.split("?")[0];
                if (activity.status === "ready" && activity.href) {
                  navigate(activity.href);
                } else if (activity.onOpen && (!destinationPath || destinationPath === location.pathname)) {
                  activity.onOpen();
                } else if (activity.href) {
                  navigate(activity.href);
                } else {
                  activity.onOpen?.();
                }
              };
              return <div className={`global-research-run global-research-run--${locked ? "locked" : activity.status === "ready" ? "ready" : activity.status === "failed" ? "failed" : "active"}`} key={activity.id}>{locked ? <div className="global-research-run__locked" role="status" aria-label={accessibleName}><LockSimple size={16} weight="bold" aria-hidden="true" />{content}</div> : activity.onOpen ? <button type="button" aria-label={accessibleName} className="global-research-run__open" onClick={openActivity}>{content}</button> : <Link aria-label={accessibleName} to={activity.href || `/companies/${encodeURIComponent(activity.companyId)}?tab=investigations`} onClick={openActivity}>{content}</Link>}</div>;
            })}
          </div>
        </section>}

        <main className="page-content" id="main-content">{children}</main>
        <footer className="site-footer">RAVEN is building a public-source company record.</footer>
      </div>
    </div>
  );
}
