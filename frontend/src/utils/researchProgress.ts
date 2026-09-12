import type { ResearchRun, ResearchStage } from "../types/research";

const pauseableStages: ResearchStage[] = [
  "AwaitingIdentitySelection",
  "AwaitingSourceSelection",
  "EvidenceReady",
  "AwaitingProfileConfirmation",
];

export function canPauseResearchStage(stage: ResearchStage) {
  return pauseableStages.includes(stage);
}

export function isFinishedResearch(run: ResearchRun) {
  return run.stage === "Completed" || run.stage === "Cancelled";
}

export function researchProgressLabel(run: ResearchRun, paused = false): string {
  if (paused) return `Paused · ${researchProgressLabel(run)}`;

  switch (run.stage) {
    case "Identifying":
      return "Preparing company identity";
    case "Discovering":
      if (run.queriesTotal > 0 && run.queriesCompleted < run.queriesTotal) {
        return `Searching public sources · query ${run.queriesCompleted + 1} of ${run.queriesTotal}`;
      }
      return "Search complete · classifying and ranking candidates";
    case "Grounding":
      return "Resolving the intended organization";
    case "AwaitingIdentitySelection":
      return "Identity choices ready · choose the intended organization";
    case "AwaitingSourceSelection":
      return `Source review ready · ${run.recommendedCandidates || run.uniqueCandidates} recommended root${(run.recommendedCandidates || run.uniqueCandidates) === 1 ? "" : "s"}`;
    case "Acquiring":
      return run.crawlTotal > 0
        ? `Reading selected evidence · ${Math.min(run.crawlCompleted, run.crawlTotal)} of ${run.crawlTotal}`
        : "Reading selected evidence";
    case "EvidenceReady":
      return `Evidence ready · ${run.documentsAdded} document${run.documentsAdded === 1 ? "" : "s"} acquired`;
    case "GeneratingProfile":
      return "Building the Company Profile";
    case "AwaitingProfileConfirmation":
      return "Profile preview ready · confirm to finish";
    case "Completed":
      return "Company Profile confirmed";
    case "Cancelled":
      return "Research cancelled";
    case "Failed":
      return "Research needs attention";
    default:
      return "Research status unavailable";
  }
}
