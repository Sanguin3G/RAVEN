const configuredApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim();
const apiBaseUrl = configuredApiBaseUrl ? configuredApiBaseUrl.replace(/\/$/, "") : "";

export class ApiError extends Error {
  constructor(public readonly status?: number) {
    super(status ? `RAVEN API request failed with status ${status}.` : "RAVEN API is unavailable.");
    this.name = "ApiError";
  }
}

function getUrl(path: string) {
  return `${apiBaseUrl}${path}`;
}

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;

  try {
    response = await fetch(getUrl(path), {
      ...init,
      headers: {
        Accept: "application/json",
        ...init?.headers,
      },
    });
  } catch {
    throw new ApiError();
  }

  if (!response.ok) {
    throw new ApiError(response.status);
  }

  try {
    return await response.json() as T;
  } catch {
    throw new ApiError(response.status);
  }
}

export function getApiErrorMessage(error: unknown, fallback: string) {
  if (error instanceof ApiError) {
    if (error.status === 404) {
      return "RAVEN could not find that company.";
    }

    return "RAVEN couldn't reach the server. Please check that the API is running and try again.";
  }

  return fallback;
}
