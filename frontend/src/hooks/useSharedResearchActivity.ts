import { useEffect, useRef } from "react";
import { getManagedResearchJobs } from "../api/managedResearch";
import { getExternalResearchAnalysis } from "../api/externalResearch";
import { getBriefingGenerationJob } from "../api/briefings";
import { getCurrentCompanyProfile } from "../api/profiles";
import { hasUsableAcceptedProfile } from "../utils/profileReadiness";
import { updateResearchActivity, useResearchActivities } from "../utils/researchActivity";

export function useSharedResearchActivity() {
  const sharedResearchActivities = useResearchActivities();
  const sharedResearchActivitiesRef = useRef(sharedResearchActivities);
  sharedResearchActivitiesRef.current = sharedResearchActivities;
  useEffect(() => {
    let active = true;
    const refreshSharedResearch = async () => {
      const tracked = sharedResearchActivitiesRef.current.filter((activity) => activity.origin === "Deep" || activity.status === "running");
      const deepCompanyIds = [...new Set(tracked.filter((activity) => activity.origin === "Deep").map((activity) => activity.companyId))];
      const externalJobs = tracked.filter((activity) => activity.origin === "External" && activity.jobId);
      const briefingJobs = tracked.filter((activity) => activity.origin === "Briefing" && activity.jobId);

      await Promise.all([
        ...deepCompanyIds.map(async (companyId) => {
          const [result, profile] = await Promise.all([
            getManagedResearchJobs(companyId).catch(() => []),
            getCurrentCompanyProfile(companyId).catch(() => null),
          ]);
          const jobs = Array.isArray(result) ? result : [];
          if (!active) return;
          jobs.forEach((job) => {
            const activity = tracked.find((item) => item.id === `deep-${job.id}`);
            if (!activity) return;
            const status: "ready" | "failed" | "running" = job.status === "Completed" ? "ready" : job.status === "Failed" || job.status === "Cancelled" ? "failed" : "running";
            const locked = status === "ready" && job.purpose === "ProfileImprovement" && !hasUsableAcceptedProfile(profile);
            updateResearchActivity(activity.id, {
              detail: locked ? "Profile required · create the company profile to unlock review" : status === "ready" ? "Ready for review" : status === "failed" ? "Deep Research could not complete" : job.status === "Queued" ? "Starting investigation" : "Researching across sources",
              status,
              locked,
              updatedAt: job.completedAt || job.createdAt,
            });
          });
        }),
        ...externalJobs.map(async (activity) => {
          const job = await getExternalResearchAnalysis(activity.companyId, activity.jobId!).catch(() => null);
          if (!active || !job) return;
          const status: "ready" | "failed" | "running" = job.status === "Completed" ? "ready" : job.status === "Failed" ? "failed" : "running";
          updateResearchActivity(activity.id, {
            detail: status === "ready" ? "Ready for review" : status === "failed" ? "Analysis failed; pasted material is preserved" : "Reading imported response",
            status,
            updatedAt: job.completedAt || job.createdAt,
          });
        }),
        ...briefingJobs.map(async (activity) => {
          const job = await getBriefingGenerationJob(activity.companyId, activity.jobId!).catch(() => null);
          if (!active || !job) return;
          const status = job.status === "Completed" ? "ready" : job.status === "Failed" ? "failed" : "running";
          const href = job.resultBriefingId
            ? `/companies/${encodeURIComponent(activity.companyId)}?tab=briefings&briefing=${encodeURIComponent(job.resultBriefingId)}`
            : activity.href;
          updateResearchActivity(activity.id, {
            detail: status === "ready" ? "Briefing is ready" : status === "failed" ? "Briefing generation failed" : "Generating briefing…",
            status,
            href,
            updatedAt: job.updatedAt,
          });
        }),
      ]);
    };

    void refreshSharedResearch();
    const intervalId = window.setInterval(() => { void refreshSharedResearch(); }, 5_000);
    return () => { active = false; window.clearInterval(intervalId); };
  }, []);
  return sharedResearchActivities;
}
