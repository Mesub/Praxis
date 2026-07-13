import { describe, expect, it } from "vitest";
import type { TeachBackResponse } from "./api";
import { outcomeOf, scoreLabel } from "./teachback";

describe("outcomeOf", () => {
  it("has a presentation for each outcome", () => {
    for (const o of ["Passed", "Failed", "Pending"] as TeachBackResponse["outcome"][]) {
      expect(outcomeOf(o).label.length).toBeGreaterThan(0);
    }
  });

  it("says an abstention was set aside, not failed", () => {
    expect(outcomeOf("Pending").gloss.toLowerCase()).toContain("not sure");
    expect(outcomeOf("Pending").gloss.toLowerCase()).toContain("never quietly a pass");
  });
});

describe("scoreLabel", () => {
  it("renders a score as whole percent", () => {
    expect(scoreLabel(0.78)).toBe("78%");
  });

  it("renders an abstention as an em dash, never a zero", () => {
    expect(scoreLabel(null)).toBe("—");
  });
});
