import { useState } from "react";
import { getResearchRun } from "../../../api/research";
import type { ResearchRun } from "../../../types/research";
import { canPauseResearchStage, researchProgressLabel } from "../../../utils/researchProgress";
import { setCurrentResearchPaused } from "../../../utils/researchSession";

export function useResearchRunLifecycle() {
  const [run, setRun] = useState<ResearchRun | null>(null);
  const [isPaused, setIsPaused] = useState(false);

  async function waitForDiscovery(runId: string) {
    let current = await getResearchRun(runId);
    setRun(current);
    while (["Identifying", "Discovering", "Grounding"].includes(current.stage) && current.status !== "Failed" && current.status !== "Cancelled") {
      await new Promise((resolve) => window.setTimeout(resolve, 900));
      current = await getResearchRun(runId);
      setRun(current);
    }
    return current;
  }

  async function waitForAcquisition(runId: string) {
    let current = await getResearchRun(runId);
    setRun(current);
    while (current.stage === "Acquiring" && current.status !== "Failed" && current.status !== "Cancelled") {
      await new Promise((resolve) => window.setTimeout(resolve, 900));
      current = await getResearchRun(runId);
      setRun(current);
    }
    return current;
  }

  const activityCounters = run
    ? {
        queriesTotal: run.queriesTotal,
        queriesCompleted: run.queriesCompleted,
        searchResultsFound: run.sourcesFound,
        uniqueCandidates: run.uniqueCandidates,
        recommendedCandidates: run.recommendedCandidates,
        sourcesSelected: run.sourcesSelected,
        crawlTotal: run.crawlTotal,
        crawlCompleted: run.crawlCompleted,
        crawlSucceeded: run.crawlSucceeded,
        crawlFailed: run.crawlFailed,
        documentsAdded: run.documentsAdded,
        duplicatesSkipped: run.duplicatesSkipped,
      }
    : undefined;
  const canPause = run ? canPauseResearchStage(run.stage) : false;
  const researchStatusDetail = run ? researchProgressLabel(run, isPaused) : null;

  function togglePause() {
    if (!run || !canPauseResearchStage(run.stage)) return;
    const next = setCurrentResearchPaused(run.id, !isPaused);
    setIsPaused(next?.paused === true);
  }

  return { run, setRun, isPaused, setIsPaused, waitForDiscovery, waitForAcquisition, activityCounters, canPause, researchStatusDetail, togglePause };
}
