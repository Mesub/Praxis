/*
  Teach-back presentation — the pure part.

  You get the verdict, not the rubric. A rubric's bands are, in practice, the answer, so they never
  cross the wire (M11.7a) and there is nothing here that could render them. What this file turns into
  pixels is the outcome, the score, and the one thing the whole feature is honest about: a language
  model graded this, so it can raise belief but can never, by itself, make you held (C-4).
*/

import type { TeachBackResponse } from "./api";

export type OutcomePresentation = {
  label: string;
  gloss: string;
  /** Tailwind text-colour token, on-brand. */
  tone: string;
};

const OUTCOME: Record<TeachBackResponse["outcome"], OutcomePresentation> = {
  Passed: {
    label: "Passed the rubric",
    gloss: "Your explanation cleared the bar. It moved belief — up to the ceiling, never past it.",
    tone: "text-good",
  },
  Failed: {
    label: "Below the bar",
    gloss: "It was graded and fell short. That is information, not a verdict on you — it is recorded, and the plan will come back to this.",
    tone: "text-serious",
  },
  Pending: {
    label: "Set aside for a human",
    gloss: "The grader was not sure this counts as proof, so it abstained. Nothing was scored against you — an honest 'not sure' is never quietly a pass.",
    tone: "text-warn",
  },
};

export function outcomeOf(outcome: TeachBackResponse["outcome"]): OutcomePresentation {
  return OUTCOME[outcome];
}

/** The score as a whole percent, or an em dash when the grader abstained. */
export function scoreLabel(score: number | null): string {
  return score === null ? "—" : `${Math.round(score * 100)}%`;
}
