/*
  The typed seam between the web console and the .NET API.

  Every PRAXIS endpoint returns one envelope shape (`ApiResponse<T>`), so there is exactly
  one thing to unwrap here — and one place that knows how to carry the bearer token, surface
  a failure honestly, and never pretend a network error was a `null` success.
*/

/** Mirrors `Praxis.Shared.ApiResponse<T>` — the one envelope every endpoint returns. */
export interface ApiResponse<T> {
  success: boolean;
  message: string | null;
  data: T | null;
  errors: string[] | null;
  timestamp: string;
}

/**
 * A call outcome the UI can branch on without a try/catch at every call site.
 * `ok` carries the unwrapped data; a failure carries the API's honest message —
 * including the network/transport failures that never reached the API at all,
 * which must never masquerade as a successful empty response.
 */
export type ApiResult<T> =
  | { ok: true; data: T }
  | { ok: false; message: string; errors: string[] };

const BASE = (process.env.NEXT_PUBLIC_API_BASE ?? "http://localhost:5000").replace(/\/$/, "");

type Options = {
  method?: "GET" | "POST" | "PUT" | "DELETE";
  body?: unknown;
  /** Bearer token for authed routes. The public Ledger verification routes need none. */
  token?: string;
  signal?: AbortSignal;
};

async function call<T>(path: string, options: Options = {}): Promise<ApiResult<T>> {
  const { method = "GET", body, token, signal } = options;

  const headers: Record<string, string> = { Accept: "application/json" };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (token) headers.Authorization = `Bearer ${token}`;

  let response: Response;
  try {
    response = await fetch(`${BASE}${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      signal,
      cache: "no-store",
    });
  } catch {
    // The request never reached the API. This is *our* problem, not a learner's — and it
    // must not be reported as anything other than what it is (mirrors the backend's rule
    // that a broken dependency is never a verdict).
    return {
      ok: false,
      message: "PRAXIS could not be reached. This is a connection problem, not your work.",
      errors: [],
    };
  }

  let envelope: ApiResponse<T> | null = null;
  try {
    envelope = (await response.json()) as ApiResponse<T>;
  } catch {
    envelope = null;
  }

  if (!response.ok || envelope === null || !envelope.success) {
    return {
      ok: false,
      message: envelope?.message ?? `Request failed (${response.status}).`,
      errors: envelope?.errors ?? [],
    };
  }

  return { ok: true, data: envelope.data as T };
}

export const api = {
  get: <T>(path: string, token?: string, signal?: AbortSignal) =>
    call<T>(path, { method: "GET", token, signal }),
  post: <T>(path: string, body: unknown, token?: string, signal?: AbortSignal) =>
    call<T>(path, { method: "POST", body, token, signal }),
};

// ── A few typed endpoints, to prove the seam and seed the pages (M11.2+) ──────

/** The Ledger's published trust root — public, no auth (the cold-start weapon). */
export interface PublicKey {
  algorithm: string;
  publicKeyBase64: string;
}

/** The verdict of checking a Verified Competency Record. */
export interface VerificationResult {
  valid: boolean;
  verifiedCompetencies: number;
  reason: string | null;
}

export const ledger = {
  publicKey: () => api.get<PublicKey>("/api/ledger/public-key"),
  verify: (record: unknown) => api.post<VerificationResult>("/api/ledger/verify", record),
};
