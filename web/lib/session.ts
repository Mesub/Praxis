/*
  The Session: a Socratic dialogue with the Answer Budget visible and ticking down.

  The tutor never gives the answer — it has none to give (the answer is never placed in its context;
  it lives behind a check_claim oracle, C-5). What it offers is a ladder of hints, each deeper one
  costing more of a budget the learner can watch drain. When the budget cannot buy the next rung, the
  session does not fail and does not surrender the answer: it asks the most valuable two sentences in
  learning — *where exactly does your reasoning break?* — and reschedules around the reply.

  This file is the pure part: the shape of the ladder, whether the next rung is affordable, whether
  the budget is exhausted, and how full the meter is. No network, no DOM.
*/

import type { SessionResponse } from "./api";

/** A rung of the hint ladder, for display context. Costs come from the API (authoritative), not here. */
export type Rung = {
  key: string;
  label: string;
  /** True for the free, unlimited orienting rung — orienting yourself is a skill you should not be charged for. */
  free: boolean;
  blurb: string;
};

export const LADDER: readonly Rung[] = [
  { key: "Orient", label: "Orient", free: true, blurb: "Restate the goal; name what is known and unknown. No new facts — free, always." },
  { key: "Probe", label: "Probe", free: false, blurb: "One Socratic question that narrows the search space." },
  { key: "Analogy", label: "Analogy", free: false, blurb: "Map it onto something you already hold." },
  { key: "WorkedExample", label: "Worked example", free: false, blurb: "A different problem, worked start to finish, thinking aloud." },
  { key: "PartialScaffold", label: "Partial scaffold", free: false, blurb: "The structure, and only the structure — with the hole still in it. Nothing is below this." },
];

/** Normalise a rung name to the camelCase the API reads (and case-insensitively accepts). */
export function toRungParam(name: string): string {
  if (name.length === 0) return name;
  return name[0].toLowerCase() + name.slice(1);
}

/** The blurb for a rung name, or a safe empty string. */
export function rungBlurb(name: string | null): string {
  return LADDER.find((r) => r.key === name)?.blurb ?? "";
}

/**
 * Exhausted when the ladder has bottomed out (no next rung) or the budget can no longer buy the next
 * rung. Either way the answer is not coming — running out was never a route to it.
 */
export function isExhausted(s: SessionResponse): boolean {
  if (s.nextRung === null) return true;
  return s.budgetRemaining < s.nextRungCost;
}

/** True when the learner can take the next rung right now. Free rungs are always affordable. */
export function canAffordNext(s: SessionResponse): boolean {
  return s.nextRung !== null && s.budgetRemaining >= s.nextRungCost;
}

/**
 * How full the budget meter is, 0..1, measured against the largest budget seen this session (its
 * starting allowance). Guards a zero or negative starting value so the meter is never NaN.
 */
export function budgetFraction(remaining: number, starting: number): number {
  if (starting <= 0) return 0;
  return Math.min(1, Math.max(0, remaining / starting));
}
