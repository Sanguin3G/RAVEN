import { request } from "./client";
import type { Investigation, InvestigationPurpose, ResearchClaim, ResearchSourceLead } from "./investigations";

export const briefingTemplates = ["Financial & Performance", "Talent & Hiring", "Business Model", "Competitive Landscape", "Markets & Expansion", "Partnerships", "Regulatory", "Supply Chain", "Custom"] as const;
export type BriefingTemplate = typeof briefingTemplates[number];

export interface BriefingSection { key: string; title: string; items: string[]; sourceInvestigationIds: string[] }
export interface BriefingSourceSnapshot {
  investigationId: string; materialKind: "Saved" | "Managed"; materialId?: string | null;
  title: string; origin: string; purpose: InvestigationPurpose; topics: string[];
  materialUpdatedAt: string; summary: string; claims: ResearchClaim[];
  sourceLeads: ResearchSourceLead[]; uncertainties: string[]; rawMaterial?: string | null;
}
export interface BriefingVersion {
  id: string; versionNumber: number; generatedAt: string; researchThrough: string;
  title: string; template: BriefingTemplate; objective: string;
  sections: BriefingSection[]; sources: BriefingSourceSnapshot[];
}
export interface Briefing {
  id: string; companyId: string; title: string; template: BriefingTemplate; objective: string;
  createdAt: string; updatedAt: string; archivedAt?: string | null;
  currentVersion: BriefingVersion; versionCount: number; newerRelevantCount: number;
}
export interface BriefingListItem {
  id: string; title: string; template: BriefingTemplate; generatedAt: string;
  researchThrough: string; versionNumber: number; sourceCount: number; newerRelevantCount: number;
}
export interface BriefingChange {
  fromVersion: number; toVersion: number; newMaterial: string[];
  changedMaterial: string[]; removedMaterial: string[]; newUncertainties: string[];
}
export interface BriefingCandidate { id: string; title: string; origin: string; purpose: string; topics: string[]; materialUpdatedAt: string }

const base = (companyId: string) => `/api/companies/${encodeURIComponent(companyId)}/briefings`;
const json = (body: unknown) => ({ method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });

export const getBriefings = (companyId: string) => request<BriefingListItem[]>(base(companyId));
export const getBriefing = (companyId: string, id: string) => request<Briefing>(`${base(companyId)}/${encodeURIComponent(id)}`);
export const getBriefingVersions = (companyId: string, id: string) => request<BriefingVersion[]>(`${base(companyId)}/${encodeURIComponent(id)}/versions`);
export const getBriefingVersion = (companyId: string, id: string, number: number) => request<BriefingVersion>(`${base(companyId)}/${encodeURIComponent(id)}/versions/${number}`);
export const getBriefingChanges = (companyId: string, id: string, number: number) => request<BriefingChange>(`${base(companyId)}/${encodeURIComponent(id)}/versions/${number}/changes`);
export const getNewerBriefingInvestigations = (companyId: string, id: string) => request<BriefingCandidate[]>(`${base(companyId)}/${encodeURIComponent(id)}/newer-investigations`);
export const createBriefing = (companyId: string, body: { title: string; template: BriefingTemplate; objective: string; investigationIds: string[] }) => request<Briefing>(base(companyId), json(body));
export const updateBriefing = (companyId: string, id: string, body: { newInvestigationIds: string[]; title?: string; template?: BriefingTemplate; objective?: string }) => request<Briefing>(`${base(companyId)}/${encodeURIComponent(id)}/versions`, json(body));

export function suggestedForTemplate(template: BriefingTemplate, investigations: Investigation[]) {
  const topics: Record<BriefingTemplate, string[]> = {
    "Financial & Performance": ["Financial & Performance"], "Talent & Hiring": ["Talent & Hiring", "Employee Scale"],
    "Business Model": ["Business Model", "Products & Services", "Markets"], "Competitive Landscape": ["Competitive Landscape", "Markets"],
    "Markets & Expansion": ["Markets", "Locations"], Partnerships: ["Strategy & Partnerships"],
    Regulatory: ["Regulatory", "Tax / Registration"], "Supply Chain": ["Supply Chain"], Custom: [],
  };
  const terms = topics[template];
  return investigations.filter(item => item.status === "Ready" || item.status === "Done").sort((a, b) =>
    Number(b.topics.some(topic => terms.includes(topic))) - Number(a.topics.some(topic => terms.includes(topic))) ||
    Date.parse(b.materialUpdatedAt) - Date.parse(a.materialUpdatedAt));
}
