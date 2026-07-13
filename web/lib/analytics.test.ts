import { describe, expect, it } from "vitest";
import type {
  AnalyticsResponse,
  CalibrationTrendReport,
  ConsistencyReport,
  DecayDebtReport,
} from "./api";
import {
  calibrationTrend,
  consistency,
  decayDebt,
  minutesLabel,
  rateLabel,
  ratePerHour,
  verdictOf,
} from "./analytics";

function trendReport(over: Partial<CalibrationTrendReport> = {}): CalibrationTrendReport {
  return {
    direction: "notEnoughData",
    earlierBrier: null,
    recentBrier: null,
    earlierSamples: 0,
    recentSamples: 0,
    ...over,
  };
}

function consistencyReport(over: Partial<ConsistencyReport> = {}): ConsistencyReport {
  return {
    pattern: "notEnoughData",
    activeDays: 0,
    spanDays: 0,
    meanGapDays: null,
    regularity: null,
    ...over,
  };
}

function debtReport(over: Partial<DecayDebtReport> = {}): DecayDebtReport {
  return {
    creditAtRisk: 0,
    retainedCredit: 0,
    conceptsDecaying: 0,
    conceptsLapsed: 0,
    hasHeldConcepts: false,
    ...over,
  };
}

function mpm(over: Partial<AnalyticsResponse> = {}): AnalyticsResponse {
  return {
    rate: 0.05,
    masteryCredit: 3,
    minutesInvested: 60,
    hasData: true,
    gainedNothing: false,
    calibration: trendReport(),
    consistency: consistencyReport(),
    decayDebt: debtReport(),
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

describe("calibrationTrend", () => {
  it("says nothing at all when there is not enough graded history", () => {
    const t = calibrationTrend(trendReport({ direction: "notEnoughData" }));
    expect(t.sentence).toBeNull();
    expect(t.tone).toBe("none");
  });

  it("celebrates an improving calibration", () => {
    const t = calibrationTrend(trendReport({ direction: "improving" }));
    expect(t.sentence?.toLowerCase()).toContain("improving");
    expect(t.tone).toBe("good");
  });

  it("flags a sliding calibration as the thing worth catching early", () => {
    const t = calibrationTrend(trendReport({ direction: "declining" }));
    expect(t.sentence?.toLowerCase()).toContain("sliding");
    expect(t.tone).toBe("bad");
  });

  it("calls a steady calibration steady, without drama", () => {
    const t = calibrationTrend(trendReport({ direction: "holdingSteady" }));
    expect(t.sentence?.toLowerCase()).toContain("holding steady");
    expect(t.tone).toBe("neutral");
  });
});

describe("consistency", () => {
  it("says nothing when there are too few active days", () => {
    const c = consistency(consistencyReport({ pattern: "notEnoughData" }));
    expect(c.sentence).toBeNull();
    expect(c.tone).toBe("none");
  });

  it("praises a steady cadence and names the spacing in plain words", () => {
    const c = consistency(consistencyReport({ pattern: "regular", meanGapDays: 2 }));
    expect(c.sentence?.toLowerCase()).toContain("about every 2 days");
    expect(c.sentence?.toLowerCase()).toContain("stick");
    expect(c.tone).toBe("good");
  });

  it("collapses a near-daily cadence to 'most days', not 'every 1 days'", () => {
    const c = consistency(consistencyReport({ pattern: "regular", meanGapDays: 1 }));
    expect(c.sentence?.toLowerCase()).toContain("most days");
  });

  it("flags cram-then-vanish without shaming a gap", () => {
    const c = consistency(consistencyReport({ pattern: "erratic" }));
    expect(c.sentence?.toLowerCase()).toContain("bursts");
    expect(c.tone).toBe("neutral");
  });
});

describe("decayDebt", () => {
  it("says nothing when the learner has proved nothing yet", () => {
    const d = decayDebt(debtReport({ hasHeldConcepts: false }));
    expect(d.headline).toBeNull();
    expect(d.tone).toBe("none");
    expect(d.offerReview).toBe(false);
  });

  it("reassures, without a review nag, when everything held is still fresh", () => {
    const d = decayDebt(debtReport({ hasHeldConcepts: true, creditAtRisk: 0, retainedCredit: 2 }));
    expect(d.headline?.toLowerCase()).toContain("nothing you have proved is slipping");
    expect(d.tone).toBe("good");
    expect(d.offerReview).toBe(false);
  });

  it("names the debt, counts what is slipping, and offers the review that pays it down", () => {
    const d = decayDebt(
      debtReport({
        hasHeldConcepts: true,
        creditAtRisk: 0.37,
        retainedCredit: 2.1,
        conceptsDecaying: 2,
        conceptsLapsed: 1,
      }),
    );
    expect(d.atRisk).toBe("0.37");
    expect(d.detail).toBe("2 decaying · 1 lapsed");
    expect(d.tone).toBe("warn");
    expect(d.offerReview).toBe(true);
  });

  it("omits a zero count from the detail line", () => {
    const d = decayDebt(
      debtReport({ hasHeldConcepts: true, creditAtRisk: 0.2, conceptsDecaying: 3, conceptsLapsed: 0 }),
    );
    expect(d.detail).toBe("3 decaying");
  });
});
