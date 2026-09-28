import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getCompany } from "../api/companies";
import { clearCompanyReturnRoute, readCompanyReturnRoute, type CompanyReturnRoute } from "../utils/companyReturnRoute";

export function CompanyReturnLink({ active }: { active: boolean }) {
  const [context, setContext] = useState<CompanyReturnRoute | null>(() => active ? readCompanyReturnRoute() : null);
  const [companyName, setCompanyName] = useState<string | null>(null);
  const [invalid, setInvalid] = useState(false);

  useEffect(() => {
    if (!active) {
      clearCompanyReturnRoute();
      setContext(null);
      setCompanyName(null);
      setInvalid(false);
      return;
    }
    const saved = readCompanyReturnRoute();
    setContext(saved);
    setCompanyName(null);
    if (!saved) { setInvalid(true); return; }
    let current = true;
    void getCompany(saved.companyId).then((company) => {
      if (current) setCompanyName(company.name);
    }).catch(() => {
      if (!current) return;
      clearCompanyReturnRoute();
      setContext(null);
      setInvalid(true);
    });
    return () => { current = false; };
  }, [active]);

  if (!active || (!context && !invalid)) return null;
  if (invalid) return <Link className="context-topbar__return" to="/companies" state={null}>← Companies</Link>;
  if (!context || !companyName) return null;
  return <Link className="context-topbar__return" to={context.route} state={{ companyReturn: true }}>← Back to {companyName}</Link>;
}
