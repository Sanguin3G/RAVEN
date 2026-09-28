import { useEffect, useRef, useState } from "react";
import { getBriefingGenerationJob, type BriefingGenerationJob } from "../../api/briefings";
import styles from "./company-briefings.module.css";

interface Props {
  companyId: string;
  title: string;
  initialJob: BriefingGenerationJob;
  onMinimize: () => void;
  onRetry: () => void;
  onCompleted: (briefingId: string) => void;
}

export function BriefingGenerationStatus({ companyId, title, initialJob, onMinimize, onRetry, onCompleted }: Props) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [job, setJob] = useState(initialJob);
  useEffect(() => { if (!dialog.current?.open) dialog.current?.showModal(); }, []);
  useEffect(() => {
    if (job.status === "Completed" && job.resultBriefingId) onCompleted(job.resultBriefingId);
  }, [job.status, job.resultBriefingId, onCompleted]);
  useEffect(() => {
    if (job.status === "Completed" || job.status === "Failed") return;
    let active = true;
    const refresh = async () => {
      const current = await getBriefingGenerationJob(companyId, job.id).catch(() => null);
      if (active && current) setJob(current);
    };
    const timer = window.setInterval(() => { void refresh(); }, 1_500);
    void refresh();
    return () => { active = false; window.clearInterval(timer); };
  }, [companyId, job.id, job.status]);

  return <dialog ref={dialog} className={styles.dialog} aria-labelledby="briefing-generation-heading" onCancel={(event) => { event.preventDefault(); onMinimize(); }}>
    <header><div><p className={styles.dialogEyebrow}>{job.operation === "Create" ? "CREATE BRIEFING" : "UPDATE BRIEFING"}</p><h2 id="briefing-generation-heading">{job.status === "Completed" ? `${title} ready` : job.status === "Failed" ? title : `${job.operation === "Create" ? "Creating" : "Updating"} “${title}”`}</h2></div><button type="button" aria-label="Minimize briefing generation" onClick={onMinimize}>×</button></header>
    {job.status === "Failed" ? <><p role="alert">{job.error || "Briefing generation failed."}</p><footer><button type="button" className="button button--quiet" onClick={onMinimize}>Close</button><button type="button" className="button" onClick={onRetry}>Retry</button></footer></>
      : job.status === "Completed" ? <p role="status">Briefing generation completed.</p>
        : <><p role="status">{job.status === "Saving" ? "Saving the new immutable version…" : "Generating briefing…"}</p><footer><span /><button type="button" className="button button--secondary" onClick={onMinimize}>Minimize</button></footer></>}
  </dialog>;
}
