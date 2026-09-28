export const companyReturnRouteKey = "raven:return-company-route";

export interface CompanyReturnRoute {
  companyId: string;
  route: string;
}

export function readCompanyReturnRoute(): CompanyReturnRoute | null {
  try {
    const value: unknown = JSON.parse(window.sessionStorage.getItem(companyReturnRouteKey) ?? "null");
    if (!value || typeof value !== "object") return null;
    const candidate = value as Partial<CompanyReturnRoute>;
    if (typeof candidate.companyId !== "string" || typeof candidate.route !== "string") return null;
    const match = candidate.route.match(/^\/companies\/([^/?#]+)(?:[/?#]|$)/);
    if (!match || decodeURIComponent(match[1]) !== candidate.companyId || candidate.companyId === "new") return null;
    return { companyId: candidate.companyId, route: candidate.route };
  } catch {
    return null;
  }
}

export function rememberCompanyReturnRoute(pathname: string, search = "", hash = ""): CompanyReturnRoute | null {
  const match = pathname.match(/^\/companies\/([^/]+)(?:\/.*)?$/);
  if (!match || match[1] === "new") return null;
  try {
    const context = { companyId: decodeURIComponent(match[1]), route: `${pathname}${search}${hash}` };
    window.sessionStorage.setItem(companyReturnRouteKey, JSON.stringify(context));
    return context;
  } catch {
    return null;
  }
}

export function clearCompanyReturnRoute() {
  try { window.sessionStorage.removeItem(companyReturnRouteKey); } catch { /* Navigation remains usable when storage is blocked. */ }
}
