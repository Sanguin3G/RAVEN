import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { ArrowLeft } from "@phosphor-icons/react";
import { getCompany } from "../api/companies";
import { clearCompanyReturnRoute, readCompanyReturnRoute, type CompanyReturnRoute } from "../utils/companyReturnRoute";

export function CompanyReturnLink({ onNavigate }: { onNavigate?: () => void }) {
  const [context, setContext] = useState<CompanyReturnRoute | null>(readCompanyReturnRoute);
  const [companyName, setCompanyName] = useState<string | null>(null);
  const [invalid, setInvalid] = useState(false);

  useEffect(() => {
    const saved = readCompanyReturnRoute();
    setContext(saved);
    setCompanyName(null);
    if (!saved) {
      setInvalid(true);
      return;
    }

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
  }, []);

  if (!context && !invalid) return null;
  if (invalid) return <Link className="sidebar-nav__return" to="/companies" state={null} onClick={onNavigate} title="Browse companies">
    <ArrowLeft size={14} weight="bold" aria-hidden="true" /><span>Companies</span>
  </Link>;
  if (!context || !companyName) return null;
  return <Link className="sidebar-nav__return" to={context.route} state={{ companyReturn: true }} onClick={onNavigate} title={`Return to ${companyName}`} aria-label={`Return to ${companyName}`}>
    <ArrowLeft size={14} weight="bold" aria-hidden="true" /><span>{companyName}</span>
  </Link>;
}
