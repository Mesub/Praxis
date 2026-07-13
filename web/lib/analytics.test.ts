import { describe, expect, it } from "vitest";
import type { AnalyticsResponse } from "./api";
import { minutesLabel, rateLabel, ratePerHour, verdictOf } from "./analytics";

function mpm(over: Partial<AnalyticsResponse> = {}): AnalyticsResponse {
  return {
    rate: 0.05,
    masteryCredit: 3,
    minutesInvested: 60,
    hasData: true,
    gainedNothing: false,
    ...over,
  };
}

describe("rate", () => {
  it("scales the per-minute rate to per-hour", () => {
    expect(ratePerHour(0.05)).toBeCloseTo(3);
  });

  it("renders a dash when there is no rate yet", () => {
    expect(rateLabel(null)).toBe("—");
    expect(rateLabel(0.05)).toBe("3.00");
  });
});

describe("minutesLabel", () => {
  it("stays in minutes while small and rolls to hours when large", () => {
    expect(minutesLabel(45)).toBe("45 min");
    expect(minutesLabel(60)).toBe("1 h");
    expect(minutesLabel(125)).toBe("2 h 5 min");
  });
});

describe("verdictOf", () => {
  it("calls no data a blank page, not a zero, and offers a next step", () => {
    const v = verdictOf(mpm({ hasData: false, rate: null, minutesInvested: 0, masteryCredit: 0 }));
    expect(v.headline.toLowerCase()).toContain("blank page");
    expect(v.needsNextStep).toBe(true);
  });

  it("says a gained-nothing stretch out loud and offers a next step", () => {
    const v = verdictOf(mpm({ gainedNothing: true, rate: 0, masteryCredit: 0, minutesInvested: 90 }));
    expect(v.headline.toLowerCase()).toContain("did not move");
    expect(v.needsNextStep).toBe(true);
  });

  it("reports a real rate plainly, with no next step nagging", () => {
    const v = verdictOf(mpm());
    expect(v.headline.toLowerCase()).toContain("honest");
    expect(v.needsNextStep).toBe(false);
  });
});
