/*
  The Contract, and the one refusal that defines PRAXIS: the Decay Wall (CANON C-1).

  Every other learning product answers "can I do this?" with an enthusiastic yes, because the yes is
  what gets paid for. PRAXIS runs a forward simulation against the minutes you actually gave, and if
  the objective does not survive being *maintained* at that pace, it says so — and offers the
  narrower objective it genuinely believes in, plus the number of minutes that would buy the whole
  thing. The plan is still shown either way; what is withheld is only the pretence.

  This file is the pure part: turning a pace + cadence into the daily number the compiler
  interrogates, and turning a returned plan into what the learner is told. No wall-clock, no DOM.
*/

import type { AlternativeResponse, PlanResponse, WallResponse } from "./api";

/** At or above this share of the day spent on maintenance, you are running to stand still. */
export const RUNNING_TO_STAND_STILL = 0.8;

/**
 * The compiler models a single daily pace, but people study in bursts. So the honest input is
 * minutes on the days you show up × days per week, averaged over all seven — because the Decay Wall
 * does not care how you feel about your schedule, only how many minutes of forward progress it sees
 * per calendar day. Guarded so a nonsense cadence cannot manufacture a pace.
 */
export function effectiveMinutesPerDay(minutesOnStudyDays: number, daysPerWeek: number): number {
  const m = Math.max(0, minutesOnStudyDays);
  const d = Math.min(7, Math.max(0, daysPerWeek));
  return Math.round((m * d) / 7);
}

/** conceptsReached / conceptsInPlan, in 0..1. 0 when the plan is empty (never NaN). */
export function reachedFraction(wall: WallResponse): number {
  if (wall.conceptsInPlan <= 0) return 0;
  return wall.conceptsReached / wall.conceptsInPlan;
}

/** The share of the peak day spent maintaining, as whole percent. */
export function maintenanceSharePercent(wall: WallResponse): number {
  return Math.round(wall.maintenanceShareOfDay * 100);
}

/** True when, at the worst point, most of the day goes on keeping what you already hold. */
export function runningToStandStill(wall: WallResponse): boolean {
  return wall.maintenanceShareOfDay >= RUNNING_TO_STAND_STILL;
}

export type Verdict = "believed" | "wall";

export function verdictOf(plan: PlanResponse): Verdict {
  return plan.believable ? "believed" : "wall";
}

/** PRAXIS's headline sentence about the plan — the yes it means, or the no it is willing to say. */
export function planHeadline(plan: PlanResponse): string {
  if (plan.believable) {
    return "PRAXIS believes this. You reach the objective at the pace you gave.";
  }
  return "PRAXIS does not believe this — not at this pace.";
}

/**
 * The Decay Wall, in one honest paragraph: how far the pace actually gets before the horizon, and
 * whether the learner is running to stand still. Only meaningful when the plan is not believed.
 */
export function wallExplanation(plan: PlanResponse): string {
  const w = plan.wall;
  const reached = `${w.conceptsReached} of ${w.conceptsInPlan} concepts`;
  const share = maintenanceSharePercent(w);
  if (runningToStandStill(w)) {
    return `At this pace you reach ${reached} before the ${w.horizonDays}-day horizon — and at the worst point ${share}% of your day goes on keeping what you already have. That is running to stand still, and no honest plan hides it.`;
  }
  return `At this pace you reach ${reached} before the ${w.horizonDays}-day horizon. At the worst point ${share}% of your day goes on maintaining what you hold, leaving the rest for progress.`;
}

/** What PRAXIS offers instead — the narrower objective it believes in, and the number that would buy the whole thing. */
export function alternativeOffer(alt: AlternativeResponse): string {
  const reachable = `${alt.conceptsReachable} of ${alt.conceptsInObjective}`;
  if (alt.minutesPerDayThatWouldWork === null) {
    return `What you can reach is ${reachable}. No daily amount reaches the whole objective inside the horizon — the objective is wrong for now, not the schedule. Narrow it, and PRAXIS will compile that.`;
  }
  return `What you can reach at this pace is ${reachable}. To reach the whole objective you would need ${alt.minutesPerDayThatWouldWork} minutes a day. Find the minutes, or let PRAXIS compile the narrower objective it believes in.`;
}
