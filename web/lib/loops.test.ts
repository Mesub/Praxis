import { describe, expect, it } from "vitest";
import type { ResolveResponse } from "./api";
import { confidenceLabel, coverageLabel, horizonLabel, KINDS, verdictLine } from "./loops";

function resolved(over: Partial<ResolveResponse> = {}): ResolveResponse {
  return {
    loopId: "l",
    correct: true,
    brierScore: 0.04,
    predicted: "4",
    groundTruth: "4",
    confidence: 0.9,
    ...over,
  };
}

describe("labels", () => {
  it("coverage is a percent, or a dash before anything closes", () => {
    expect(coverageLabel(0.75)).toBe("75%");
    expect(coverageLabel(null)).toBe("—");
  });

  it("confidence is a whole percent", () => {
    expect(confidenceLabel(0.8)).toBe("80%");
  });

  it("names an overdue loop the debt it is, and today as resolve-now", () => {
    expect(horizonLabel({ resolvesToday: false, isOverdue: true, horizonDays: -2 })).toBe("overdue");
    expect(horizonLabel({ resolvesToday: true, isOverdue: false, horizonDays: 0 })).toBe("resolve now");
    expect(horizonLabel({ resolvesToday: false, isOverdue: false, horizonDays: 3 })).toBe("in 3 days");
  });

  it("offers exactly the two code-loop kinds", () => {
    expect(KINDS.map((k) => k.value)).toEqual(["Output", "Raises"]);
  });
});

describe("verdictLine — the Brier, in words", () => {
  it("names sure-and-wrong as the most expensive kind", () => {
    expect(verdictLine(resolved({ correct: false, confidence: 0.9 })).toLowerCase()).toContain(
      "most expensive",
    );
  });

  it("credits sure-and-right as calibration", () => {
    expect(verdictLine(resolved({ correct: true, confidence: 0.9 })).toLowerCase()).toContain(
      "calibration",
    );
  });

  it("calls an unsure wrong answer a cheap mistake, not a failure", () => {
    expect(verdictLine(resolved({ correct: false, confidence: 0.55 })).toLowerCase()).toContain(
      "cheap",
    );
  });

  it("notes underclaiming when right but unsure", () => {
    expect(verdictLine(resolved({ correct: true, confidence: 0.55 })).toLowerCase()).toContain(
      "hedged",
    );
  });
});
