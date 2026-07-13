import { describe, expect, it } from "vitest";
import type { DecayForecastEntry, TwinSummaryResponse } from "./api";
import {
  calibrationVerdict,
  confidenceGapPoints,
  forecastUrgency,
  STATES,
} from "./graph";

function summary(over: Partial<TwinSummaryResponse> = {}): TwinSummaryResponse {
  return {
    conceptsHeld: 40,
    conceptsInProgress: 8,
    conceptsDecaying: 5,
    conceptsLapsed: 3,
    overallBrierScore: 0.18,
    overallConfidenceGap: 0.0,
    calibrationIsMeaningful: true,
    activeMisconceptions: 2,
    decayForecast: [],
    ...over,
  };
}

function entry(over: Partial<DecayForecastEntry> = {}): DecayForecastEntry {
  return { conceptId: "c", pKnows: 0.8, retrievability: 0.6, daysUntilDue: 10, alreadyLapsed: false, ...over };
}

describe("the four states", () => {
  it("cover held, in-progress, decaying and lapsed, each pulling its own count", () => {
    expect(STATES.map((s) => s.key)).toEqual(["held", "inProgress", "decaying", "lapsed"]);
    const s = summary();
    expect(STATES.find((x) => x.key === "decaying")!.count(s)).toBe(5);
    expect(STATES.find((x) => x.key === "lapsed")!.count(s)).toBe(3);
  });

  it("does not soften the lapsed gloss — you think you know these", () => {
    const lapsed = STATES.find((x) => x.key === "lapsed")!;
    expect(lapsed.gloss.toLowerCase()).toContain("you think you know these");
  });
});

describe("forecastUrgency", () => {
  it("is lapsed when already below the line, whatever the days say", () => {
    expect(forecastUrgency(entry({ alreadyLapsed: true, daysUntilDue: 30 }))).toBe("lapsed");
  });

  it("is soon within a week, later beyond it", () => {
    expect(forecastUrgency(entry({ daysUntilDue: 7 }))).toBe("soon");
    expect(forecastUrgency(entry({ daysUntilDue: 8 }))).toBe("later");
  });
});

describe("calibrationVerdict", () => {
  it("refuses to read a gap it has not earned", () => {
    expect(calibrationVerdict(summary({ calibrationIsMeaningful: false })).toLowerCase()).toContain(
      "not enough forecasts",
    );
  });

  it("names overconfidence in points", () => {
    const text = calibrationVerdict(summary({ overallConfidenceGap: 0.12 }));
    expect(text).toContain("12 points ahead");
  });

  it("names underconfidence too", () => {
    expect(calibrationVerdict(summary({ overallConfidenceGap: -0.1 })).toLowerCase()).toContain(
      "underclaiming",
    );
  });

  it("renders the gap as whole points", () => {
    expect(confidenceGapPoints(0.086)).toBe(9);
  });
});
