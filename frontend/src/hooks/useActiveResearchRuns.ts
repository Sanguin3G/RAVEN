import { useEffect, useState } from "react";
import { cancelResearchRun, getActiveResearchRuns, getResearchRun } from "../api/research";
import type { ActiveResearchRun } from "../types/research";
import { clearCurrentResearch, readCurrentResearch, rememberCurrentResearch, setCurrentResearchPaused, type CurrentResearchSession } from "../utils/researchSession";
import { canPauseResearchStage, isFinishedResearch } from "../utils/researchProgress";

export function useActiveResearchRuns() {
  const [activeResearch, setActiveResearch] = useState<ActiveResearchRun[]>([]);
  const [currentResearchSession, setCurrentResearchSession] = useState<CurrentResearchSession | null>(readCurrentResearch);
  useEffect(() => {
    let active = true;
    const refreshActiveResearch = async () => {
      const serverRuns = await getActiveResearchRuns().catch(() => null);
      if (!active) return;

      let session = readCurrentResearch();
      let rememberedRun: ActiveResearchRun | null = null;

      if (session) {
        const serverRun = serverRuns?.find((item) => item.run.id === session?.runId);
        const run = serverRun?.run ?? await getResearchRun(session.runId).catch(() => null);
        if (run) {
          if (isFinishedResearch(run)) {
            clearCurrentResearch(session.runId);
            session = null;
          } else {
            if (!canPauseResearchStage(run.stage) && session.paused) {
              setCurrentResearchPaused(run.id, false);
              session = readCurrentResearch();
            }
            rememberedRun = { run, companyName: session?.companyName ?? "Company research" };
          }
        }
      }

      if (!session && serverRuns && serverRuns.length > 0) {
        const first = serverRuns[0];
        rememberCurrentResearch(first.run, first.companyName);
        session = readCurrentResearch();
      }

      const merged = [...(Array.isArray(serverRuns) ? serverRuns : [])];
      if (rememberedRun && !merged.some((item) => item.run.id === rememberedRun?.run.id)) {
        merged.unshift(rememberedRun);
      }
      setCurrentResearchSession(session);
      setActiveResearch(merged);
    };
    void refreshActiveResearch();
    const timer = window.setInterval(refreshActiveResearch, 2_500);
    return () => { active = false; window.clearInterval(timer); };
  }, []);

  async function cancelActiveResearch(runId: string) {
    try {
      await cancelResearchRun(runId);
      clearCurrentResearch(runId);
      setCurrentResearchSession(readCurrentResearch());
      setActiveResearch((current) => current.filter((item) => item.run.id !== runId));
    } catch {
      // The destination page remains the source of truth if cancellation fails.
    }
  }

  function togglePauseResearch(runId: string) {
    const item = activeResearch.find((entry) => entry.run.id === runId);
    if (!item || !canPauseResearchStage(item.run.stage)) return;
    const isPaused = currentResearchSession?.runId === runId && currentResearchSession.paused;
    const next = setCurrentResearchPaused(runId, !isPaused);
    setCurrentResearchSession(next);
  }

  return { activeResearch, currentResearchSession, cancelActiveResearch, togglePauseResearch };
}
