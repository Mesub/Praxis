/*
  Loops — the pure part.

  A Reality Loop's value is irrevocable commitment: you say what will happen and how sure you are,
  and then it stands. So the presentation never offers a way to withdraw one, names an overdue loop
  as the debt it is, and — after the fact — reports the Brier honestly: sure-and-wrong is the most
  expensive score there is, and it is only sayable because the confidence was frozen first.
*/

import type { ResolveResponse } from "./api";

export const KINDS = [
  { value: "Output", label: "What will it print?", hint: "Give the exact output." },
  { value: "Raises", label: "Will it raise an error?", hint: "Answer 'clean', or 'raises' and the exception type." },
] as const;

export type Kind = (typeof KINDS)[number]["value"];

/** Coverage as a whole percent, or a dash when nothing has closed yet. */
export function coverageLabel(coverage: number | null): string {
  return coverage === null ? "—" : `${Math.round(coverage * 100)}%`;
}

/** Confidence as a whole percent. */
export function confidenceLabel(confidence: number): string {
  return `${Math.round(confidence * 100)}%`;
}

/** How far off resolution is, in plain words. */
export function horizonLabel(loop: { resolvesToday: boolean; isOverdue: boolean; horizonDays: number }): string {
  if (loop.isOverdue) return "overdue";
  if (loop.resolvesToday) return "resolve now";
  const d = loop.horizonDays;
  return d === 1 ? "in 1 day" : `in ${d} days`;
}

/**
 * The one honest line about a resolved prediction. Being wrong is not the sin — being *confidently*
 * wrong is, and being unsure and wrong is nearly free. The Brier says which.
 */
export function verdictLine(r: ResolveResponse): string {
  const sure = r.confidence >= 0.7;
  if (r.correct && sure) {
    return "Right, and you were sure. That is what calibration looks like.";
  }
  if (r.correct && !sure) {
    return "Right — but you hedged. You knew more than you let yourself claim.";
  }
  if (!r.correct && sure) {
    return "Wrong, and you were sure. That is the most expensive kind of wrong there is.";
  }
  return "Wrong — but you were not sure, and saying so was honest. Cheap mistake.";
}
