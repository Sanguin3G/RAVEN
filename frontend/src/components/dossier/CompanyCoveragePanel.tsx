import type { CoverageLevel, EvidenceCoverageItem, EvidenceCoverageResponse, ResearchTarget } from "../../api/coverage";
import styles from "./dossier.module.css";
import type { DossierCoverage } from "./dossierTypes";

export interface CompanyCoveragePanelProps {
  coverage?: DossierCoverage | EvidenceCoverageResponse | null;
  compact?: boolean;
}

const targetOrder: ResearchTarget[] = [
  "LegalIdentity",
  "TaxRegistration",
  "FoundedHistory",
  "Industry",
  "EmployeeScale",
  "ProductsServices",
  "Markets",
  "Leadership",
  "Locations",
];

const targetLabels: Record<ResearchTarget, string> = {
  LegalIdentity: "Legal identity",
  TaxRegistration: "Tax registration",
  FoundedHistory: "Founded / history",
  Industry: "Industry",
  EmployeeScale: "Company scale",
  ProductsServices: "Products / services",
  Markets: "Markets",
  Leadership: "Leadership",
  Locations: "Locations",
};

const levelDescriptions: Record<CoverageLevel, string> = {
  Missing: "No usable evidence",
  Weak: "Limited or low-authority evidence",
  Supported: "At least one useful source",
  Strong: "Authoritative, corroborated evidence",
};

function normaliseCoverage(coverage: CompanyCoveragePanelProps["coverage"]): { response?: EvidenceCoverageResponse | null; isLoading: boolean; error?: string | null } {
  if (!coverage) return { isLoading: false };
  if ("companyId" in coverage) return { response: coverage, isLoading: false };
  const state = coverage as DossierCoverage;
  return { response: state.response, isLoading: state.isLoading ?? false, error: state.error };
}

function marker(level: CoverageLevel) {
  return level === "Strong" ? "●" : level === "Supported" ? "◐" : level === "Weak" ? "◌" : "○";
}

function levelClass(level: CoverageLevel) {
  return `${styles.coverageLevel} ${styles[`coverageLevel${level}`]}`;
}

function coverageItem(items: EvidenceCoverageItem[], target: ResearchTarget): EvidenceCoverageItem {
  return items.find((item) => item.target === target) ?? {
    target,
    level: "Missing",
    supportingSourceCount: 0,
    strongestSourceKind: null,
    reasons: [],
  };
}

export function CompanyCoveragePanel({ coverage, compact = false }: CompanyCoveragePanelProps) {
  const state = normaliseCoverage(coverage);
  const response = state.response;
  const items = response?.items ?? [];

  return (
    <section className={`${styles.coveragePanel} ${compact ? styles.coveragePanelCompact : ""}`} data-testid="company-coverage" aria-labelledby="company-coverage-heading" aria-busy={state.isLoading || undefined}>
      <div className={styles.coverageHeader}>
        <div>
          <p className={styles.eyebrow}>Evidence quality</p>
          <h2 id="company-coverage-heading">Research coverage</h2>
        </div>
        {response?.budgetExhausted && <span className={styles.coverageBudget}>Budget reached</span>}
      </div>
      <p className={styles.coverageIntro}>Qualitative support for the accepted dossier. RAVEN keeps unsupported fields unknown instead of inventing a confidence score.</p>

      {state.isLoading && <p className={styles.contextNote} role="status">Loading evidence coverage…</p>}
      {state.error && <p className={styles.errorMessage} role="alert">{state.error}</p>}
      {!state.isLoading && !state.error && !response && <p className={styles.contextNote}>Coverage is not available for this profile yet.</p>}
      {response && (
        <ul className={styles.coverageList}>
          {targetOrder.map((target) => {
            const item = coverageItem(items, target);
            const reason = item.reasons[0] || levelDescriptions[item.level];
            return (
              <li className={styles.coverageItem} key={target}>
                <span className={levelClass(item.level)} aria-hidden="true">{marker(item.level)}</span>
                <span className={styles.coverageLabel}>{targetLabels[target]}</span>
                <span className={styles.coverageValue} title={reason}>
                  <strong>{item.level}</strong>
                  <small>{item.supportingSourceCount ? `${item.supportingSourceCount} source${item.supportingSourceCount === 1 ? "" : "s"}` : levelDescriptions[item.level]}</small>
                </span>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
