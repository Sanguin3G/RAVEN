import { useEffect, useRef, useState } from "react";
import { getResearchSettings } from "../../../api/settings";
import type { CompanyMatchResponse } from "../../../types/company";
import type { GroundingMode, ResearchIdentityCandidate } from "../../../types/research";
import type { IdentityResolutionResponse, ResolvedIdentitySnapshot } from "../../../types/identity";
import type { GroundingOverride, IdentityForm } from "../types";
import { initialForm } from "./researchWorkflowUtils";

export function useIdentityResolutionWorkflow() {
  const [form, setForm] = useState<IdentityForm>(initialForm);
  const [groundingOverride, setGroundingOverride] = useState<GroundingOverride>("default");
  const [defaultGroundingMode, setDefaultGroundingMode] = useState<GroundingMode>("Auto");
  const [matches, setMatches] = useState<CompanyMatchResponse[]>([]);
  const [preflightResponse, setPreflightResponse] = useState<IdentityResolutionResponse | null>(null);
  const [identityGuidance, setIdentityGuidance] = useState<IdentityResolutionResponse | null>(null);
  const [identityGuidanceOpen, setIdentityGuidanceOpen] = useState(false);
  const [selectedPreflightEntityId, setSelectedPreflightEntityId] = useState<string | null>(null);
  const [pendingResolvedIdentity, setPendingResolvedIdentity] = useState<ResolvedIdentitySnapshot | null>(null);
  const [identityCandidates, setIdentityCandidates] = useState<ResearchIdentityCandidate[]>([]);
  const [selectedIdentityCandidateId, setSelectedIdentityCandidateId] = useState<string | null>(null);
  const [alternateIdentityHint, setAlternateIdentityHint] = useState("");
  const identityHeadingRef = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    getResearchSettings()
      .then((settings) => {
        if (["Auto", "Always", "Off"].includes(settings.groundingMode)) setDefaultGroundingMode(settings.groundingMode);
      })
      .catch(() => undefined);
  }, []);

  function openIdentityGuidance() {
    if (identityGuidance) setIdentityGuidanceOpen(true);
  }

  function closeIdentityGuidance() {
    setIdentityGuidanceOpen(false);
  }

  return {
    form, setForm, groundingOverride, setGroundingOverride, defaultGroundingMode,
    matches, setMatches, preflightResponse, setPreflightResponse,
    identityGuidance, setIdentityGuidance, identityGuidanceOpen, setIdentityGuidanceOpen,
    selectedPreflightEntityId, setSelectedPreflightEntityId,
    pendingResolvedIdentity, setPendingResolvedIdentity,
    identityCandidates, setIdentityCandidates, selectedIdentityCandidateId, setSelectedIdentityCandidateId,
    alternateIdentityHint, setAlternateIdentityHint, identityHeadingRef,
    openIdentityGuidance, closeIdentityGuidance,
  };
}
