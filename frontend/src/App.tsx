import { Route, Routes } from "react-router-dom";
import { AppShell } from "./components/AppShell";
import { CompanyListPage } from "./pages/CompanyListPage";
import { CompanyDetailPage } from "./pages/CompanyDetailPage";
import { DashboardPage } from "./pages/DashboardPage";
import { AddCompanyProfilePage } from "./pages/AddCompanyProfilePage";
import { NotFoundPage } from "./pages/NotFoundPage";
import { SettingsPage } from "./pages/SettingsPage";
import { StatusPage } from "./pages/StatusPage";

export function App() {
  return (
    <AppShell>
      <Routes>
        <Route path="/" element={<DashboardPage />} />
        <Route path="/companies" element={<CompanyListPage />} />
        <Route path="/companies/new" element={<AddCompanyProfilePage />} />
        <Route path="/companies/:id" element={<CompanyDetailPage />} />
        <Route path="/settings" element={<SettingsPage />} />
        <Route path="/status" element={<StatusPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </AppShell>
  );
}
