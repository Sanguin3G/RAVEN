import { useState } from "react";
import type { ResearchTarget } from "../../../types/research";

export function useProfileStrengtheningWorkflow() {
  const [strengtheningTargets, setStrengtheningTargets] = useState<ResearchTarget[]>([]);
  const [strengthenMethod, setStrengthenMethod] = useState<"raven" | "deep" | "external">("raven");

  function toggleStrengtheningTarget(target: ResearchTarget) {
    setStrengtheningTargets((current) => current.includes(target)
      ? current.filter((item) => item !== target)
      : [...current, target]);
  }

  return { strengtheningTargets, setStrengtheningTargets, strengthenMethod, setStrengthenMethod, toggleStrengtheningTarget };
}
