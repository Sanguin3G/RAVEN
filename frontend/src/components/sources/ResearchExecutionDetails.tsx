import { useEffect, useState } from "react";
import { getResearchExecution } from "../../api/research";
import type { ResearchExecution } from "../../types/research";

const seconds = (milliseconds: number) => `${(milliseconds / 1000).toFixed(2)} sec`;

/** Developer-oriented telemetry; deliberately separate from the user activity timeline. */
export function ResearchExecutionDetails({ researchRunId }: { researchRunId: string }) {
  const [execution, setExecution] = useState<ResearchExecution | null>(null);
  useEffect(() => { void getResearchExecution(researchRunId).then(setExecution).catch(() => setExecution(null)); }, [researchRunId]);
  if (!execution) return null;
  const { summary, operations } = execution;
  return <details className="research-execution-details">
    <summary>Execution details · {seconds(summary.totalWallClockDurationMs)}</summary>
    <p>{summary.searchCalls} search · {summary.crawlCalls} crawl · {summary.aiCalls} AI · {summary.providerAttempts} provider attempts · {summary.fallbacks} fallbacks</p>
    {summary.inputTokens != null || summary.outputTokens != null ? <p>{summary.inputTokens ?? "—"} input · {summary.outputTokens ?? "—"} output tokens</p> : null}
    <ul>{operations.map((operation) => <li key={operation.id}><strong>{operation.category} · {operation.operation}</strong>{operation.provider ? ` · ${operation.provider}` : ""}{operation.model ? ` · ${operation.model}` : ""}{operation.durationMs != null ? ` · ${seconds(operation.durationMs)}` : ""}</li>)}</ul>
  </details>;
}
