import type { CompanyMergePreview } from "../../api/workspace";
import { WorkspaceDialog } from "./WorkspaceDialog";

export function MergePreviewDialog({ open, preview, loading, error, busy, onClose, onConfirm }: { open: boolean; preview: CompanyMergePreview | null; loading: boolean; error: string | null; busy: boolean; onClose: () => void; onConfirm: () => void }) {
  return <WorkspaceDialog open={open} title="Review company merge" labelledBy="merge-dialog-title" onClose={onClose}>
    {loading ? <p className="state-message">Preparing a safe merge preview…</p> : null}
    {error ? <div className="form-error" role="alert">{error}</div> : null}
    {preview ? <>
      <p className="workspace-dialog__intro">RAVEN will keep the canonical record and move compatible research history to it. Nothing changes until you confirm.</p>
      <div className="merge-comparison">
        <section className="merge-comparison__side"><p className="eyebrow">KEEP</p><h3>{preview.canonicalCompanyName}</h3><p>Canonical company record</p><small>{preview.profileVersions} profile version{preview.profileVersions === 1 ? "" : "s"} · {preview.sourceDocuments} source document{preview.sourceDocuments === 1 ? "" : "s"}</small></section>
        <div className="merge-comparison__arrow" aria-hidden="true">→</div>
        <section className="merge-comparison__side merge-comparison__side--duplicate"><p className="eyebrow">MERGE FROM</p><h3>{preview.duplicateCompanyName}</h3><p>Duplicate record</p><small>{preview.researchRuns} research run{preview.researchRuns === 1 ? "" : "s"} · {preview.savedInvestigations} saved investigation{preview.savedInvestigations === 1 ? "" : "s"}</small></section>
      </div>
      <section className="merge-preservation"><h3>RAVEN will preserve</h3><ul><li>Research history and profile versions</li><li>Unique sources and evidence provenance</li><li>Saved investigations and compatible monitoring state</li></ul>{preview.warnings.map((warning) => <p key={warning} className="merge-warning">{warning}</p>)}</section>
      <div className="modal-actions"><button className="button button--secondary" type="button" onClick={onClose} disabled={busy}>Cancel</button><button className="button" type="button" onClick={onConfirm} disabled={busy}>{busy ? "Merging…" : "Merge companies"}</button></div>
    </> : null}
    {!loading && !preview && !error ? <p className="state-message">No merge preview is available.</p> : null}
  </WorkspaceDialog>;
}
