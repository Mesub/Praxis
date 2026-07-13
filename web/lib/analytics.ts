/*
  Analytics — the pure part.

  Mastery-per-Minute is the one number PRAXIS is judged by, and the presentation's job is to keep it
  honest: a rate over no time is "not yet", not zero; a stretch where nothing was learned is said out
  loud, not hidden; and when the number is bad, the very next line points at the single smallest thing
  that would change it (R-3) — never shame, always a next step.
*/

import type { AnalyticsResponse, CalibrationTrendReport } from "./api";

/** The raw per-minute rate is tiny; per *hour* is what a person can hold in their head. */
export function ratePerHour(rate: number): number {
  return rate * 60;
}

/** Mastery-per-hour, to one decimal — or a dash when there is no rate yet. */
export function rateLabel(rate: number | null): string {
  return rate === null ? "—" : ratePerHour(rate).toFixed(2);
}

/** Minutes shown as hours and minutes once they get large, plain minutes while small. */
export function minutesLabel(minutes: number): string {
  const m = Math.round(minutes);
  if (m < 60) return `${m} min`;
  const h = Math.floor(m / 60);
  const rem = m % 60;
  return rem === 0 ? `${h} h` : `${h} h ${rem} min`;
}

export type Verdict = {
  headline: string;
  /** True when the honest thing to do is point the learner at their next step rather than a number. */
  needsNextStep: boolean;
};

/** The one honest sentence about the trade — and whether to follow it with a next step. */
export function verdictOf(a: AnalyticsResponse): Verdict {
  if (!a.hasData) {
    return {
      headline:
        "There is nothing to measure yet — you have not invested any time. This is not a zero; it is a blank page.",
      needsNextStep: true,
    };
  }
  if (a.gainedNothing) {
    return {
      headline: `You invested ${minutesLabel(a.minutesInvested)} and your verified mastery did not move. That is worth knowing — most apps would never tell you.`,
      needsNextStep: true,
    };
  }
  return {
    headline: `Every hour you invested bought about ${ratePerHour(a.rate as number).toFixed(2)} of verified, decay-adjusted mastery. That is the number that matters, and it is honest.`,
    needsNextStep: false,
  };
}

export type CalibrationTrend = {
  /** Null when there is not enough graded history to say anything — the screen then shows nothing,
   *  rather than a reassuring flat line. */
  sentence: string | null;
  /** For styling: a worsening trend is the one worth flagging. */
  tone: "good" | "neutral" | "bad" | "none";
};

/** Whether the learner is getting better at knowing what they know — said plainly, or not at all. */
export function calibrationTrend(trend: CalibrationTrendReport): CalibrationTrend {
  switch (trend.direction) {
    case "improving":
      return {
        sentence:
          "Your calibration is improving — you are getting better at knowing what you actually know. That is the meta-skill that matters most.",
        tone: "good",
      };
    case "declining":
      return {
        sentence:
          "Your calibration is sliding — lately you have been more sure than you should be. That is worth catching early, before it becomes a habit.",
        tone: "bad",
      };
    case "holdingSteady":
      return {
        sentence: "Your calibration is holding steady — no meaningful drift either way.",
        tone: "neutral",
      };
    case "notEnoughData":
    default:
      return { sentence: null, tone: "none" };
  }
}
