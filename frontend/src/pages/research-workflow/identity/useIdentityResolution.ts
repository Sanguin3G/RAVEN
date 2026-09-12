import { useCallback, useMemo, useRef, useState } from "react";
import { getApiErrorMessage } from "../../../api/client";
import { resolveCompanyIdentity, toIdentityResolutionRequest } from "../../../api/identity";
import type {
  IdentityInputField,
  IdentityOption,
  IdentityResolutionRequest,
  IdentityResolutionResponse,
  IdentityResolutionStatus,
  IdentityWorkflowState,
  ResolvedIdentitySnapshot,
} from "../../../types/identity";

export interface UseIdentityResolutionOptions {
  initialInput?: Partial<IdentityResolutionRequest>;
}

export interface IdentityResolutionController {
  input: IdentityResolutionRequest;
  response: IdentityResolutionResponse | null;
  state: IdentityWorkflowState;
  loading: boolean;
  error: string | null;
  selectedEntityId: string | null;
  selectedEntity: IdentityOption | null;
  updateInput: (field: IdentityInputField, value: string) => void;
  setInput: (input: IdentityResolutionRequest) => void;
  resolve: (nextInput?: Partial<IdentityResolutionRequest>) => Promise<IdentityResolutionResponse | null>;
  retry: () => Promise<IdentityResolutionResponse | null>;
  selectEntity: (temporaryId: string) => void;
  reset: () => void;
  confirmExactName: () => IdentityResolutionResponse | null;
}

const blankInput: IdentityResolutionRequest = { name: "" };

function stateForStatus(status: IdentityResolutionStatus): IdentityWorkflowState {
  switch (status) {
    case "Resolved": return "resolved";
    case "Ambiguous": return "ambiguous";
    case "NeedsMoreInfo": return "needsMoreInfo";
    case "Unknown": return "unknown";
  }
}

function exactTemporaryId(name: string) {
  const slug = name.toLocaleLowerCase().replace(/[^\p{L}\p{N}]+/gu, "-").replace(/^-|-$/g, "").slice(0, 60);
  return `exact-${slug || "company"}`;
}

/**
 * Focused state boundary for the pre-search identity step. It owns retries and
 * preserves every user-entered hint; the parent workflow only needs to decide
 * what to do after a target is resolved.
 */
export function useIdentityResolution(options: UseIdentityResolutionOptions = {}): IdentityResolutionController {
  const initialInput: IdentityResolutionRequest = {
    ...blankInput,
    ...options.initialInput,
  };
  const [input, setInputState] = useState<IdentityResolutionRequest>(initialInput);
  const [response, setResponse] = useState<IdentityResolutionResponse | null>(null);
  const [state, setState] = useState<IdentityWorkflowState>("idle");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selectedEntityId, setSelectedEntityId] = useState<string | null>(null);
  const attemptRef = useRef(0);

  const updateInput = useCallback((field: IdentityInputField, value: string) => {
    setInputState((current) => ({ ...current, [field]: value }));
    setError(null);
  }, []);

  const setInput = useCallback((nextInput: IdentityResolutionRequest) => {
    setInputState(nextInput);
    setError(null);
  }, []);

  const resolve = useCallback(async (nextInput?: Partial<IdentityResolutionRequest>) => {
    const candidate = { ...input, ...nextInput };
    const requestInput = toIdentityResolutionRequest(candidate);
    setInputState(candidate);
    setResponse(null);
    setSelectedEntityId(null);
    setError(null);

    if (!requestInput.name) {
      setState("failed");
      setError("Enter a company name before checking its identity.");
      return null;
    }

    const attempt = ++attemptRef.current;
    setState("resolving");
    setLoading(true);

    try {
      const nextResponse = await resolveCompanyIdentity(requestInput);
      if (attempt !== attemptRef.current) return null;

      setResponse(nextResponse);
      setState(stateForStatus(nextResponse.status));
      setSelectedEntityId(nextResponse.recommendedEntityId ?? (nextResponse.entities.length === 1 ? nextResponse.entities[0].temporaryId : null));
      return nextResponse;
    } catch (reason: unknown) {
      if (attempt !== attemptRef.current) return null;
      setState("failed");
      setError(getApiErrorMessage(reason, "RAVEN couldn't confidently resolve this organization right now."));
      return null;
    } finally {
      if (attempt === attemptRef.current) setLoading(false);
    }
  }, [input]);

  const retry = useCallback(() => resolve(), [resolve]);

  const selectEntity = useCallback((temporaryId: string) => {
    setSelectedEntityId(temporaryId);
    setError(null);
  }, []);

  const reset = useCallback(() => {
    attemptRef.current += 1;
    setInputState(initialInput);
    setResponse(null);
    setState("idle");
    setLoading(false);
    setError(null);
    setSelectedEntityId(null);
  }, [initialInput]);

  const confirmExactName = useCallback((): IdentityResolutionResponse | null => {
    const requestInput = toIdentityResolutionRequest(input);
    if (!requestInput.name) {
      setState("failed");
      setError("Enter a company name before continuing.");
      return null;
    }

    const exactEntity: IdentityOption = {
      temporaryId: exactTemporaryId(requestInput.name),
      displayName: requestInput.name,
      legalName: requestInput.legalName ?? null,
      country: requestInput.country ?? null,
      region: null,
      officialDomain: requestInput.website ?? null,
      entityType: "Unknown",
      parentTemporaryId: null,
      relationshipToQuery: "Exact",
      confidence: "Medium",
      shortDescription: "The exact name and hints supplied by you will be used for public research.",
    };
    const manualResponse: IdentityResolutionResponse = {
      status: "Resolved",
      ambiguityType: "None",
      recommendedEntityId: exactEntity.temporaryId,
      entities: [exactEntity],
      requestedHints: [],
      message: "You chose to research this exact name. Public sources will determine the verified identity.",
      resolutionMethod: "UserConfirmedExactInput",
    };
    setResponse(manualResponse);
    setSelectedEntityId(exactEntity.temporaryId);
    setState("resolved");
    setError(null);
    return manualResponse;
  }, [input]);

  const selectedEntity = useMemo(
    () => response?.entities.find((entity) => entity.temporaryId === selectedEntityId) ?? null,
    [response, selectedEntityId],
  );

  return {
    input,
    response,
    state,
    loading,
    error,
    selectedEntityId,
    selectedEntity,
    updateInput,
    setInput,
    resolve,
    retry,
    selectEntity,
    reset,
    confirmExactName,
  };
}

export function toResolvedIdentitySnapshot(
  response: IdentityResolutionResponse,
  entity: IdentityOption | null | undefined,
): ResolvedIdentitySnapshot | null {
  if (response.status !== "Resolved" || !entity) return null;
  const parent = entity.parentTemporaryId
    ? response.entities.find((candidate) => candidate.temporaryId === entity.parentTemporaryId)
    : undefined;
  return {
    displayName: entity.displayName,
    country: entity.country ?? null,
    region: entity.region ?? null,
    legalNameHint: entity.legalName ?? null,
    officialDomainHint: entity.officialDomain ?? null,
    entityType: entity.entityType,
    parentName: parent?.displayName ?? null,
    resolutionMethod: response.resolutionMethod,
  };
}
