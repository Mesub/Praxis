import { describe, expect, it } from "vitest";
import type { SessionResponse } from "./api";
import {
  budgetFraction,
  canAffordNext,
  isExhausted,
  LADDER,
  rungBlurb,
  toRungParam,
} from "./session";

function session(over: Partial<SessionResponse> = {}): SessionResponse {
  return {
    sessionId: "s1",
    conceptId: "c1",
    state: "Active",
    outcome: null,
    budgetRemaining: 40,
    nextRungCost: 6,
    nextRung: "Probe",
    hintsTaken: 1,
    produced: 0,
    madeSomething: false,
    awaitingYou: true,
    ...over,
  };
}

describe("the ladder", () => {
  it("starts free and bottoms out at the partial scaffold", () => {
    expect(LADDER[0].free).toBe(true);
    expect(LADDER[LADDER.length - 1].key).toBe("PartialScaffold");
  });

  it("camel-cases a rung name for the wire", () => {
    expect(toRungParam("WorkedExample")).toBe("workedExample");
    expect(toRungParam("Probe")).toBe("probe");
  });

  it("has a blurb for each rung, and an empty string for the unknown", () => {
    expect(rungBlurb("Probe").length).toBeGreaterThan(0);
    expect(rungBlurb(null)).toBe("");
  });
});

describe("affordability and exhaustion — the visible scarcity", () => {
  it("can afford the next rung when the budget covers it", () => {
    expect(canAffordNext(session({ budgetRemaining: 6, nextRungCost: 6 }))).toBe(true);
    expect(canAffordNext(session({ budgetRemaining: 5, nextRungCost: 6 }))).toBe(false);
  });

  it("is exhausted when the budget cannot buy the next rung", () => {
    expect(isExhausted(session({ budgetRemaining: 5, nextRungCost: 6 }))).toBe(true);
  });

  it("is exhausted at the bottom of the ladder, even with budget to spare", () => {
    expect(isExhausted(session({ nextRung: null, nextRungCost: 0, budgetRemaining: 99 }))).toBe(true);
  });

  it("is not exhausted while an affordable rung remains", () => {
    expect(isExhausted(session({ budgetRemaining: 40, nextRungCost: 14, nextRung: "Analogy" }))).toBe(false);
  });
});

describe("budgetFraction — the meter", () => {
  it("is remaining over starting, clamped and NaN-free", () => {
    expect(budgetFraction(20, 40)).toBe(0.5);
    expect(budgetFraction(50, 40)).toBe(1); // never over-full
    expect(budgetFraction(10, 0)).toBe(0); // never divide by zero
  });
});
