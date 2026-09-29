import type { ComponentType, MouseEvent, RefObject } from "react";
import { Link } from "react-router-dom";
import { Desktop, GearSix, Info, Moon, Palette, Question, Sun, UserCircle, type IconProps } from "@phosphor-icons/react";
import type { ThemePreference } from "../../app/theme";
import type { AuthUser } from "../../features/auth/authApi";

type Props = {
  accountMenuRef: RefObject<HTMLDivElement | null>;
  accountMenuButtonRef: RefObject<HTMLButtonElement | null>;
  isAccountMenuOpen: boolean;
  setAccountMenuOpen: (open: boolean | ((current: boolean) => boolean)) => void;
  preference: ThemePreference;
  resolvedTheme: string;
  setPreference: (preference: ThemePreference) => void;
  navigateGlobal: (event: MouseEvent<HTMLAnchorElement>, destination: string) => void;
  user: AuthUser;
  onLogout: () => void;
};

const quickThemes: Array<{ value: ThemePreference; label: string; icon: ComponentType<IconProps> }> = [
  { value: "system", label: "System", icon: Desktop },
  { value: "light", label: "Light", icon: Sun },
  { value: "dark", label: "Dark", icon: Moon },
];

export function AccountMenu({ accountMenuRef, accountMenuButtonRef, isAccountMenuOpen, setAccountMenuOpen, preference, resolvedTheme, setPreference, navigateGlobal, user, onLogout }: Props) {
  return (
        <div className="account-menu" ref={accountMenuRef}>
          {isAccountMenuOpen && (
            <div className="account-menu__panel" id="account-menu-panel">
              <div className="account-menu__heading">
                <span className="account-menu__avatar" aria-hidden="true"><UserCircle size={23} weight="fill" /></span>
                <span><strong>{user.displayName}</strong><small>{user.email}</small></span>
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
                <Link to="/settings" state={null} onClick={(event) => navigateGlobal(event, "/settings")}><GearSix size={17} weight="duotone" /> Settings</Link>
                <Link to="/help" state={null} onClick={(event) => navigateGlobal(event, "/help")}><Question size={17} weight="duotone" /> Help</Link>
                <Link to="/about" state={null} onClick={(event) => navigateGlobal(event, "/about")}><Info size={17} weight="duotone" /> About RAVEN</Link>
              </div>
              <div className="account-menu__links"><button type="button" onClick={onLogout}>Sign out</button></div>
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
            <span className="account-menu__trigger-copy"><strong>{user.displayName}</strong><small>Account & appearance</small></span>
          </button>
        </div>
  );
}
