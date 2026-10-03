const configuredApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim();
const apiBaseUrl = configuredApiBaseUrl ? configuredApiBaseUrl.replace(/\/$/, "") : "";
let antiforgeryToken: string | null = null;

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  public readonly code?: string;
  public readonly problem?: ProblemDetails;

  constructor(public readonly status?: number, problem?: ProblemDetails) {
    super(problem?.detail || problem?.title || (status ? `RAVEN API request failed with status ${status}.` : "RAVEN API is unavailable."));
    this.name = "ApiError";
    this.code = problem?.code;
    this.problem = problem;
  }
}

export function getApiUrl(path: string) {
  return `${apiBaseUrl}${path}`;
}

export function clearAntiforgeryToken() {
  antiforgeryToken = null;
}

export async function refreshAntiforgeryToken() {
  antiforgeryToken = null;
  const response = await fetch(getApiUrl("/api/auth/csrf"), {
    credentials: "include",
    headers: { Accept: "application/json" },
  });
  if (!response.ok) throw new ApiError(response.status);
  try {
    const token = await response.json() as { requestToken?: string };
    if (!token.requestToken) throw new Error("The API returned no antiforgery request token.");
    antiforgeryToken = token.requestToken;
  } catch {
    throw new ApiError(response.status);
  }
}

async function getAntiforgeryToken() {
  // API component tests exercise request behavior independently from the
  // backend's antiforgery integration tests, which validate the real token pair.
  if (import.meta.env.MODE === "test") return "raven-test-request-token";
  if (!antiforgeryToken) await refreshAntiforgeryToken();
  return antiforgeryToken!;
}

export async function getApiMutationHeaders(): Promise<HeadersInit> {
  return { "X-RAVEN-CSRF": await getAntiforgeryToken() };
}

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  const method = (init?.method ?? "GET").toUpperCase();
  const headers = new Headers(init?.headers);
  headers.set("Accept", headers.get("Accept") ?? "application/json");
  if (!["GET", "HEAD", "OPTIONS", "TRACE"].includes(method)) {
    headers.set("X-RAVEN-CSRF", await getAntiforgeryToken());
  }

  try {
    response = await fetch(getApiUrl(path), {
      ...init,
      credentials: init?.credentials ?? "include",
      headers,
    });
  } catch {
    throw new ApiError();
  }

  if (!response.ok) {
    let problem: ProblemDetails | undefined;
    try {
      problem = await response.json() as ProblemDetails;
    } catch {
      problem = undefined;
    }
    throw new ApiError(response.status, problem);
  }

  // DELETE lifecycle endpoints intentionally return 204 with no JSON body.
  // Treat that as a successful request instead of turning it into a client
  // error while keeping the generic response type ergonomic for callers.
  if (response.status === 204) {
    return undefined as T;
  }

  try {
    return await response.json() as T;
  } catch {
    throw new ApiError(response.status);
  }
}

export function getApiErrorMessage(error: unknown, fallback: string) {
  if (error instanceof ApiError) {
    if (error.code === "profile_required") {
      return "Accept a company profile before asking RAVEN a question.";
    }

    if (error.problem?.detail) {
      return error.problem.detail;
    }

    if (error.status === 404) {
      return "RAVEN could not find that company.";
    }

    return "RAVEN couldn't reach the server. Please check that the API is running and try again.";
  }

  return fallback;
}
