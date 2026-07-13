/*
  The Graph, as the learner's own mind — the pure part.

  The one number nobody else in the industry shows you is *decay*: concepts you hold today and will
  not hold soon. PRAXIS shows it, and it does not flatter. Four states, and they do not pretend to be
  a tidy partition — "held" and "decaying" overlap on purpose, because a thing can be above the line
  today and falling. The honest gloss on each is the point.
*/

import type { DecayForecastEntry, TwinSummaryResponse } from "./api";

export type MindState = "held" | "inProgress" | "decaying" | "lapsed";

export type StatePresentation = {
  key: MindState;
  label: string;
  gloss: string;
  tone: string;
  count: (s: TwinSummaryResponse) => number;
};

export const STATES: readonly StatePresentation[] = [
  {
    key: "held",
    label: "Held",
    gloss: "Above the threshold today, on evidence — the frontier, and the only thing PRAXIS lets you enjoy is watching it advance.",
    tone: "text-good",
    count: (s) => s.conceptsHeld,
  },
  {
    key: "inProgress",
    label: "In progress",
    gloss: "You have started — there is evidence, but not yet enough to hold.",
    tone: "text-s1",
    count: (s) => s.conceptsInProgress,
  },
  {
    key: "decaying",
    label: "Decaying",
    gloss: "Held today, below the line soon. This is the number nobody else shows you.",
    tone: "text-warn",
    count: (s) => s.conceptsDecaying,
  },
  {
    key: "lapsed",
    label: "Lapsed",
    gloss: "Already fallen below. You think you know these, and you do not — not any more.",
    tone: "text-critical",
    count: (s) => s.conceptsLapsed,
  },
];

export type Urgency = "lapsed" | "soon" | "later";

/** Within a week counts as soon; already below the line is its own, louder thing. */
export function forecastUrgency(entry: DecayForecastEntry): Urgency {
  if (entry.alreadyLapsed) return "lapsed";
  if (entry.daysUntilDue <= 7) return "soon";
  return "later";
}

/** The confidence gap in whole points. Positive means you are, on the whole, overconfident. */
export function confidenceGapPoints(gap: number): number {
  return Math.round(gap * 100);
}

/** A short, honest read on the calibration gap — or a refusal to read one that has no basis. */
export function calibrationVerdict(summary: TwinSummaryResponse): string {
  if (!summary.calibrationIsMeaningful) {
    return "Not enough forecasts yet to say whether your confidence is honest. PRAXIS will not pretend a number it has not earned.";
  }
  const points = confidenceGapPoints(summary.overallConfidenceGap);
  if (points >= 5) {
    return `Across everything, your confidence runs about ${points} points ahead of your competence. That gap is what costs you.`;
  }
  if (points <= -5) {
    return `Across everything, you know about ${Math.abs(points)} points more than you trust yourself to. You are underclaiming.`;
  }
  return "Across everything, your confidence and your competence track closely. That honesty is rare.";
}
