import { describe, expect, it } from "vitest";
import type { AlternativeResponse, PlanResponse, WallResponse } from "./api";
import {
  alternativeOffer,
  effectiveMinutesPerDay,
  maintenanceSharePercent,
  planHeadline,
  reachedFraction,
  runningToStandStill,
  verdictOf,
  wallExplanation,
} from "./contract";

function wall(over: Partial<WallResponse> = {}): WallResponse {
  return {
    reachable: true,
    conceptsReached: 40,
    conceptsInPlan: 40,
    daysToObjective: 120,
    horizonDays: 540,
    peakMaintenanceMinutesPerDay: 10,
    maintenanceShareOfDay: 0.2,
    ...over,
  };
}

function plan(over: Partial<PlanResponse> = {}): PlanResponse {
  return {
    objective: "obj",
    steps: [],
    totalMinutes: 1000,
    conceptsAlreadyHeld: 5,
    conceptsNeedingRefresh: 2,
    daysAtStatedPace: 120,
    believable: true,
    wall: wall(),
    alternative: null,
    ...over,
  };
}

describe("effectiveMinutesPerDay — cadence folded into a daily pace", () => {
  it("averages minutes-on-study-days across the week", () => {
    // 60 minutes, 4 days a week ≈ 34/day averaged over 7.
    expect(effectiveMinutesPerDay(60, 4)).toBe(34);
    // Seven days a week is the pace itself.
    expect(effectiveMinutesPerDay(45, 7)).toBe(45);
  });

  it("cannot be inflated by a nonsense cadence", () => {
    expect(effectiveMinutesPerDay(60, 99)).toBe(60); // clamped to 7 days
    expect(effectiveMinutesPerDay(-10, 5)).toBe(0); // clamped to non-negative
  });
});

describe("reading the wall", () => {
  it("reachedFraction is NaN-free on an empty plan", () => {
    expect(reachedFraction(wall({ conceptsInPlan: 0, conceptsReached: 0 }))).toBe(0);
  });

  it("reports the maintenance share as whole percent", () => {
    expect(maintenanceSharePercent(wall({ maintenanceShareOfDay: 0.86 }))).toBe(86);
  });

  it("flags running-to-stand-still only past the threshold", () => {
    expect(runningToStandStill(wall({ maintenanceShareOfDay: 0.79 }))).toBe(false);
    expect(runningToStandStill(wall({ maintenanceShareOfDay: 0.8 }))).toBe(true);
  });
});

describe("the verdict — PRAXIS's willingness to say no", () => {
  it("believes a believable plan", () => {
    const p = plan({ believable: true });
    expect(verdictOf(p)).toBe("believed");
    expect(planHeadline(p).toLowerCase()).toContain("believes this");
  });

  it("refuses an unbelievable one, plainly", () => {
    const p = plan({ believable: false });
    expect(verdictOf(p)).toBe("wall");
    expect(planHeadline(p).toLowerCase()).toContain("does not believe");
  });

  it("names running-to-stand-still in the wall explanation when the day is mostly maintenance", () => {
    const p = plan({
      believable: false,
      wall: wall({ conceptsReached: 22, conceptsInPlan: 40, maintenanceShareOfDay: 0.9 }),
    });
    const text = wallExplanation(p).toLowerCase();
    expect(text).toContain("22 of 40");
    expect(text).toContain("running to stand still");
  });
});

describe("the alternative offer", () => {
  function alt(over: Partial<AlternativeResponse> = {}): AlternativeResponse {
    return {
      steps: [],
      minutesPerDayThatWouldWork: 45,
      conceptsReachable: 22,
      conceptsInObjective: 40,
      ...over,
    };
  }

  it("names the reachable subset and the minutes that would buy the whole thing", () => {
    const text = alternativeOffer(alt());
    expect(text).toContain("22 of 40");
    expect(text).toContain("45 minutes a day");
  });

  it("says the objective is wrong, not the schedule, when no day is enough", () => {
    const text = alternativeOffer(alt({ minutesPerDayThatWouldWork: null })).toLowerCase();
    expect(text).toContain("no daily amount");
    expect(text).toContain("objective is wrong");
  });
});
