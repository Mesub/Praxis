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

// ── The adaptive diagnostic (E6) ──────────────────────────────────────────────
//
// Mirrors `NextItemResponse` / `AnswerResponse` exactly — note what is *absent*: no answer, no
// solution, no explanation. The wire has never carried one, so this type cannot describe one.

/** The next question, or an honest reason PRAXIS has stopped asking. */
export interface NextItemResponse {
  itemId: string | null;
  stem: string | null;
  evidenceType: string | null;
  /** What PRAXIS believes about the ability, and how sure it is — always together. */
  theta: number;
  standardError: number;
  confidentEnoughToStop: boolean;
  /** True when PRAXIS has stopped asking — a 200, never an error. */
  stopped: boolean;
  says: string | null;
}

/** The truth about the learner's *own* answer — right or not, and the belief that produced it. */
export interface AnswerResponse {
  outcome: "Correct" | "Partial" | "Wrong";
  misconception: string | null;
  /** Confidence was captured *before* the outcome — that ordering is what makes this honest (C-6). */
  youSaidYouWereSure: boolean;
  andYouWere: "right" | "not";
}

export interface AnswerRequest {
  itemId: string;
  /** The learner's words — data, sent to the oracle as a claim to check, never as instructions. */
  claim: string;
  /** Stated before they are told anything, 0..1. */
  statedConfidence: number;
  seconds: number;
}

export const diagnostics = {
  nextItem: (sessionId: string, conceptId: string, token?: string, signal?: AbortSignal) =>
    api.get<NextItemResponse>(
      `/api/assessment/diagnostics/${sessionId}/next-item?conceptId=${conceptId}`,
      token,
      signal,
    ),
  submitAnswer: (sessionId: string, body: AnswerRequest, token?: string, signal?: AbortSignal) =>
    api.post<AnswerResponse>(
      `/api/assessment/diagnostics/${sessionId}/answers`,
      body,
      token,
      signal,
    ),
};

// ── The Compiler (E3): the Objective, and the plan it does or does not believe in ─────────────

export interface SetObjectiveRequest {
  conceptId: string;
  /** Taken literally — the Decay Wall is run against this number, not against optimism. */
  minutesPerDay: number;
  /** ISO date, or null for "no deadline". */
  deadline: string | null;
}

export interface ObjectiveResponse {
  id: string;
  conceptId: string;
  minutesPerDay: number;
  deadline: string | null;
  replaced: number;
}

export interface PlanStepResponse {
  conceptId: string;
  slug: string;
  position: number;
  minutes: number;
  cumulativeMinutes: number;
  dayReached: number;
  kind: string;
}

/** The forward simulation — shown to the learner, never hidden behind the verdict. */
export interface WallResponse {
  reachable: boolean;
  conceptsReached: number;
  conceptsInPlan: number;
  daysToObjective: number;
  horizonDays: number;
  peakMaintenanceMinutesPerDay: number;
  /** At the worst point, this share of the daily minutes goes on keeping what you already have. */
  maintenanceShareOfDay: number;
}

/** What PRAXIS offers instead when it does not believe — the narrower Objective it *does* believe in. */
export interface AlternativeResponse {
  steps: PlanStepResponse[];
  /** The minutes/day that would actually reach the full Objective. Null when no day is enough. */
  minutesPerDayThatWouldWork: number | null;
  conceptsReachable: number;
  conceptsInObjective: number;
}

export interface PlanResponse {
  objective: string;
  steps: PlanStepResponse[];
  totalMinutes: number;
  conceptsAlreadyHeld: number;
  conceptsNeedingRefresh: number;
  daysAtStatedPace: number;
  /** False means PRAXIS does not believe you get there at the pace you gave (C-1). The plan is still yours. */
  believable: boolean;
  wall: WallResponse;
  alternative: AlternativeResponse | null;
}

export const compiler = {
  setObjective: (body: SetObjectiveRequest, token?: string, signal?: AbortSignal) =>
    api.post<ObjectiveResponse>("/api/compiler/objectives", body, token, signal),
  plan: (token?: string, signal?: AbortSignal) =>
    api.get<PlanResponse>("/api/compiler/plan", token, signal),
};
