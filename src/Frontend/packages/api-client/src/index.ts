export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errors?: Record<string, string[]>;
  [extension: string]: unknown;
}

export interface Page<T> {
  items: T[];
  continuationToken?: string;
}

export class ApiError extends Error {
  constructor(public readonly status: number, public readonly problem?: ProblemDetails) {
    super(problem?.title ?? `Request failed with status ${status}`);
    this.name = "ApiError";
  }
}

export interface ApiClientOptions {
  baseUrl?: string;
  fetch?: typeof globalThis.fetch;
  correlationId?: () => string | undefined;
  onUnauthorized?: () => void;
}

export interface RequestOptions extends Omit<RequestInit, "body"> {
  body?: unknown;
}

export function createApiClient(options: ApiClientOptions = {}) {
  const fetcher = options.fetch ?? globalThis.fetch;
  const request = async <T>(path: string, init: RequestOptions = {}): Promise<T> => {
    const headers = new Headers(init.headers);
    // Browser clients authenticate only through the owning portal's BFF cookie.
    headers.delete("Authorization");
    headers.delete("Cookie");
    headers.set("Accept", "application/json");
    const correlationId = options.correlationId?.();
    if (correlationId) headers.set("X-Correlation-ID", correlationId);
    if (init.body !== undefined) headers.set("Content-Type", "application/json");

    const response = await fetcher(`${options.baseUrl ?? ""}${path}`, {
      ...init,
      credentials: "same-origin",
      headers,
      body: init.body === undefined ? undefined : JSON.stringify(init.body)
    });
    if (response.status === 401) options.onUnauthorized?.();
    if (!response.ok) {
      const problem = response.headers.get("content-type")?.includes("json")
        ? await response.json() as ProblemDetails
        : undefined;
      throw new ApiError(response.status, problem);
    }
    if (response.status === 204) return undefined as T;
    return await response.json() as T;
  };
  return { request };
}
