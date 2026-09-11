import { request } from "./client";

export type MonitoringCadence = "Daily" | "Weekly" | "Monthly";
export type MonitoringRunStatus = "Running" | "ReadyForReview" | "Completed" | "Failed" | "Cancelled";

export interface CompanyMonitoring {
  companyId: string;
  enabled: boolean;
  cadence: MonitoringCadence;
  nextRunAt?: string | null;
  lastRunAt?: string | null;
  lastRunStatus?: MonitoringRunStatus | null;
}

export interface UpdateCompanyMonitoring {
  enabled: boolean;
  cadence: MonitoringCadence;
}

export function getCompanyMonitoring(companyId: string) {
  return request<CompanyMonitoring>(`/api/companies/${encodeURIComponent(companyId)}/monitoring`);
}

export function updateCompanyMonitoring(companyId: string, update: UpdateCompanyMonitoring) {
  return request<CompanyMonitoring>(`/api/companies/${encodeURIComponent(companyId)}/monitoring`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(update),
  });
}
