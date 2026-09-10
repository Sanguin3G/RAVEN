import { EvidenceCard, type EvidenceRecord } from "../sources/EvidenceCard";
import styles from "./dossier.module.css";
import type { DossierSource } from "./dossierTypes";

export interface CompanySourcesTabProps {
  sources?: DossierSource[] | null;
}

function toEvidence(source: DossierSource): EvidenceRecord {
  return {
    id: source.id,
    url: source.url,
    title: source.title || source.domain || "Untitled source",
    domain: source.domain,
    kind: source.kind,
    iconUrl: source.iconUrl,
    preview: source.preview,
    retrievedAt: source.retrievedAt,
    crawlerProvider: source.crawlerProvider,
    status: source.status,
    statusMessage: source.statusMessage,
  };
}

export function CompanySourcesTab({ sources }: CompanySourcesTabProps) {
  const evidence = sources ?? [];

  return (
    <section className={styles.section} data-testid="dossier-sources" aria-labelledby="dossier-sources-heading">
      <div className={styles.sectionHeader}><h2 id="dossier-sources-heading">Sources</h2><span>{evidence.length} evidence item{evidence.length === 1 ? "" : "s"}</span></div>
      <p className={styles.tabIntro}>Acquired public material remains available for inspection. Search results are discovery signals; the cards below represent the evidence RAVEN retained.</p>
      {evidence.length ? <div className={styles.sourcesGrid}>{evidence.map((source) => <EvidenceCard key={source.id} evidence={toEvidence(source)} />)}</div> : <div className={styles.emptyState}><h2>No evidence acquired yet</h2><p>Run research and acquire selected candidates before generating a grounded Company Profile.</p></div>}
    </section>
  );
}
