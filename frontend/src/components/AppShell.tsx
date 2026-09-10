import { NavLink } from "react-router-dom";
import type { ReactNode } from "react";

export function AppShell({ children }: { children: ReactNode }) {
  return (
    <div className="app-shell">
      <header className="site-header">
        <div className="site-header__inner">
          <NavLink className="brand" to="/" aria-label="RAVEN home">
            <span className="brand__mark" aria-hidden="true">R</span>
            <span>
              <strong>RAVEN</strong>
              <small>Research, Analysis, Verification &amp; Enterprise Navigation</small>
            </span>
          </NavLink>
          <nav className="primary-nav" aria-label="Primary navigation">
            <NavLink to="/">Dashboard</NavLink>
            <NavLink to="/companies" end>Company List</NavLink>
            <NavLink to="/companies/new">Add Company Profile</NavLink>
          </nav>
          <NavLink className="settings-link" to="/settings">Settings</NavLink>
        </div>
      </header>
      <main className="page-content">{children}</main>
      <footer className="site-footer">RAVEN is building a public-source company record.</footer>
    </div>
  );
}
