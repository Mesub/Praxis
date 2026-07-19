import { describe, expect, it } from "vitest";
import {
  HARD_TRUTH,
  INITIAL_ONBOARDING,
  OBJECTIVES,
  onboardingReady,
  searchObjectives,
} from "./onboarding";

describe("searchObjectives", () => {
  it("returns the whole catalog for an empty or whitespace query", () => {
    expect(searchObjectives("")).toHaveLength(OBJECTIVES.length);
    expect(searchObjectives("   ")).toHaveLength(OBJECTIVES.length);
  });

  it("matches on title, case-insensitively", () => {
    const hits = searchObjectives("RADIOLOGIST");
    expect(hits).toHaveLength(1);
    expect(hits[0].title).toBe("Diagnostic Radiologist");
  });

  it("matches on a keyword the title does not contain", () => {
    // "quant" appears only in the Options Trader keywords, not its title or role prose.
    const hits = searchObjectives("quant");
    expect(hits.map((o) => o.id)).toEqual(["obj-options-trader"]);
  });

  it("returns nothing for a query that matches no Objective", () => {
    expect(searchObjectives("underwater basket weaving")).toHaveLength(0);
  });
});

describe("onboardingReady — the self-select-hard gate (R-3)", () => {
  it("is false in the initial state", () => {
    expect(onboardingReady(INITIAL_ONBOARDING)).toBe(false);
  });

  it("is false with a choice but no affirmation", () => {
    expect(
      onboardingReady({ selectedId: OBJECTIVES[0].id, acknowledgedHardTruth: false }),
    ).toBe(false);
  });

  it("is false with affirmation but no choice", () => {
    expect(onboardingReady({ selectedId: null, acknowledgedHardTruth: true })).toBe(false);
  });

  it("is false when the affirmation is present but the id is not a real Objective", () => {
    expect(
      onboardingReady({ selectedId: "obj-does-not-exist", acknowledgedHardTruth: true }),
    ).toBe(false);
  });

  it("is true only with a real choice AND an explicit affirmation", () => {
    expect(
      onboardingReady({ selectedId: OBJECTIVES[0].id, acknowledgedHardTruth: true }),
    ).toBe(true);
  });
});

describe("the hard truth", () => {
  it("says out loud that PRAXIS will tell you when you are failing", () => {
    expect(HARD_TRUTH.toLowerCase()).toContain("failing");
  });
});

describe("everyday english objective", () => {
  it('search finds it by "english" and by "grammar"', () => {
    expect(searchObjectives("english").map((o) => o.id)).toContain("obj-everyday-english");
    expect(searchObjectives("grammar").map((o) => o.id)).toContain("obj-everyday-english");
  });

  it("carries an honest cost line, not a brochure", () => {
    const english = OBJECTIVES.find((o) => o.id === "obj-everyday-english");
    expect(english?.difficulty).toBe("Serious");
    expect(english?.cost.length).toBeGreaterThan(20);
  });
});
