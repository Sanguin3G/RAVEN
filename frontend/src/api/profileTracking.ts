import { request } from "./client";
import type { CompanyProfileVersion } from "../types/profile";

export type ProfileChangeType = "Added" | "Removed" | "Changed";

export interface ProfileChange {
  id: string;
  companyId: string;
  fromVersion: number | null;
  toVersion: number;
  fieldPath: string;
  itemKey?: string | null;
  changeType: ProfileChangeType;
  oldValueJson?: string | null;
  newValueJson?: string | null;
  detectedAt: string;
}

export function getCompanyProfileVersions(companyId: string) {
  return request<CompanyProfileVersion[]>(`/api/companies/${encodeURIComponent(companyId)}/profile/versions`);
}

export function getCompanyProfileChanges(companyId: string) {
  return request<ProfileChange[]>(`/api/companies/${encodeURIComponent(companyId)}/profile/changes`);
}
