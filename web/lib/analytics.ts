/*
  Analytics — the pure part.

  Mastery-per-Minute is the one number PRAXIS is judged by, and the presentation's job is to keep it
  honest: a rate over no time is "not yet", not zero; a stretch where nothing was learned is said out
  loud, not hidden; and when the number is bad, the very next line points at the single smallest thing
  that would change it (R-3) — never shame, always a next step.
*/

import type {
  AnalyticsResponse,
  CalibrationTrendReport,
  ConsistencyReport,
  DecayDebtReport,
} from "./api";

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

export type RateLever = {
  /** The single biggest thing holding the rate down, named from real signals. */
  why: string;
  /** The one action that would lift it, or null when the lever is just "space your practice out". */
  fix: { label: string; href: string } | null;
};

/**
 * **Why is the rate what it is — and the one lever that would lift it.**
 *
 * The honest dashboard does not just show a number and a pile of disconnected cards; it connects the
 * headline to its biggest drag. This reads only *real, categorical* signals the engines already
 * computed — lapsed concepts, an erratic cadence, concepts slipping — never an arbitrary "you're low"
 * bar, because there is no honest absolute threshold for mastery-per-hour. It returns null when
 * nothing is dragging the rate: then the number is simply what it honestly is, and the screen says
 * nothing rather than inventing a problem.
 *
 * The blank-page and gained-nothing states own their own headline (and their own next step), so this
 * stays quiet for them.
 */
export function rateLever(a: AnalyticsResponse): RateLever | null {
  if (!a.hasData || a.gainedNothing) {
    return null;
  }

  const debt = a.decayDebt;

  // Lapsed mastery is the heaviest drag: hours spent re-earning what you already had never compound
  // into the rate. This is the first thing to fix.
  if (debt.hasHeldConcepts && debt.conceptsLapsed > 0) {
    const n = debt.conceptsLapsed;
    return {
      why: `${n} ${n === 1 ? "concept has" : "concepts have"} lapsed — some of your hours are going to re-earning mastery you already had, which never lifts the rate. Spacing your reviews keeps it from slipping.`,
      fix: { label: "Review what has lapsed", href: "/reps" },
    };
  }

  // Cramming: the same minutes, spread out, buy more. No single link fixes it — it is a habit.
  if (a.consistency.pattern === "erratic") {
    return {
      why: "Your practice comes in bursts. The same minutes, spread out, buy more mastery per hour — spacing beats cramming, and it does not ask for more of your time.",
      fix: null,
    };
  }

  // Concepts slipping but not yet lapsed: a smaller, cheaper-to-fix drag.
  if (debt.hasHeldConcepts && debt.conceptsDecaying > 0) {
    return {
      why: "A few concepts are starting to slip. A short review now keeps them from costing you re-learning time — and dragging this number down — later.",
      fix: { label: "Review what is slipping", href: "/reps" },
    };
  }

  return null;
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

export type DecayDebt = {
  /** Null when the learner holds nothing yet — the screen shows nothing rather than a hollow zero. */
  headline: string | null;
  /** The debt, to two decimals, or null when there is nothing held. */
  atRisk: string | null;
  /** A count line ("2 decaying · 1 lapsed"), or null when nothing is slipping. */
  detail: string | null;
  tone: "good" | "warn" | "none";
  /** True when review would actually restore something — the screen offers the reps link. */
  offerReview: boolean;
};

/** How much of what you proved has slipped, said as a debt owed to your future self — never as shame,
 *  always with the review that pays it down. */
export function decayDebt(report: DecayDebtReport): DecayDebt {
  if (!report.hasHeldConcepts) {
    return { headline: null, atRisk: null, detail: null, tone: "none", offerReview: false };
  }

  const slipping = report.conceptsDecaying + report.conceptsLapsed;
  if (report.creditAtRisk <= 0 || slipping === 0) {
    return {
      headline: "Nothing you have proved is slipping right now — it is all still fresh. Come back before it starts to.",
      atRisk: report.creditAtRisk.toFixed(2),
      detail: null,
      tone: "good",
      offerReview: false,
    };
  }

  const parts: string[] = [];
  if (report.conceptsDecaying > 0) parts.push(`${report.conceptsDecaying} decaying`);
  if (report.conceptsLapsed > 0) parts.push(`${report.conceptsLapsed} lapsed`);

  return {
    headline:
      "This much of what you proved has quietly slipped down the forgetting curve. It is not gone — a few minutes of review buys it back.",
    atRisk: report.creditAtRisk.toFixed(2),
    detail: parts.join(" · "),
    tone: "warn",
    offerReview: true,
  };
}

export type Consistency = {
  /** Null when there are too few active days to describe a cadence — the screen then shows nothing. */
  sentence: string | null;
  tone: "good" | "neutral" | "none";
};

/** "About every N days" — rounded to something a person says out loud. */
function cadencePhrase(meanGapDays: number): string {
  const g = Math.round(meanGapDays);
  if (g <= 1) return "most days";
  return `about every ${g} days`;
}

/** Whether the learner returns at a cadence that makes learning stick — said plainly, never as a
 *  streak, and never shaming a gap. */
export function consistency(report: ConsistencyReport): Consistency {
  switch (report.pattern) {
    case "regular":
      return {
        sentence: `You come back ${cadencePhrase(
          report.meanGapDays ?? 0,
        )} — a steady cadence. Spaced practice like this is what makes learning stick, far more than cramming.`,
        tone: "good",
      };
    case "erratic":
      return {
        sentence:
          "Your practice comes in bursts with long silent gaps. Cramming feels productive but fades fast — a steadier cadence would hold more, without asking for more of your time.",
        tone: "neutral",
      };
    case "notEnoughData":
    default:
      return { sentence: null, tone: "none" };
  }
}
