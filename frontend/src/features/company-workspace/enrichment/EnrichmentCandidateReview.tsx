import type { Dispatch, SetStateAction } from "react";
import { CandidateSourceCard } from "../../../components/sources";
import type { ResearchCandidate } from "../../../types/research";
import { toCandidateSource } from "./enrichmentMapping";
import styles from "../company-workspace.module.css";

type Props = {
  candidates: ResearchCandidate[];
  selectedCandidates: number;
  loading: boolean;
  setCandidates: Dispatch<SetStateAction<ResearchCandidate[]>>;
  acquire: () => Promise<void>;
  onBack: () => void;
};

export function EnrichmentCandidateReview({ candidates, selectedCandidates, loading, setCandidates, acquire, onBack }: Props) {
  return <section className={styles.enrichmentSection} aria-labelledby="targeted-sources-heading">
    <div className={styles.sectionHeader}><h3 id="targeted-sources-heading">Review targeted candidates</h3><span>{selectedCandidates} selected · {candidates.length} found</span></div>
    <p className={styles.contextNote}>These roots were selected for the approved gaps. Review them before they become evidence.</p>
    <div className={styles.enrichmentCandidateGrid}>{candidates.map((candidate) => <CandidateSourceCard key={candidate.id} candidate={toCandidateSource(candidate)} disabled={loading} onSelectionChange={(selected) => setCandidates((current) => current.map((item) => item.id === candidate.id ? { ...item, selected } : item))} />)}</div>
    <div className="form-actions"><button className="button" type="button" onClick={() => void acquire()} disabled={loading || selectedCandidates === 0}>Acquire {selectedCandidates} evidence root{selectedCandidates === 1 ? "" : "s"}</button><button className="button button--secondary" type="button" onClick={onBack}>Back to targets</button></div>
  </section>;
}
