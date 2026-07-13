import { describe, expect, it } from "vitest";
import { minutesLabel, retrievabilityLabel, urgencyOf } from "./reps";

describe("urgencyOf", () => {
  it("maps the urgencies case-insensitively, ripe to lost", () => {
    expect(urgencyOf("Ripe").label).toBe("Ripe");
    expect(urgencyOf("rotting").label).toBe("Rotting");
    expect(urgencyOf("LOST").label).toBe("Lost");
  });

  it("degrades gracefully on an unknown urgency rather than throwing", () => {
    expect(urgencyOf("wat").label).toBe("wat");
  });
});

describe("retrievabilityLabel", () => {
  it("is a whole percent", () => {
    expect(retrievabilityLabel(0.74)).toBe("74%");
  });
});

describe("minutesLabel", () => {
  it("rounds, and never goes negative", () => {
    expect(minutesLabel(12.6)).toBe("13 min");
    expect(minutesLabel(-1)).toBe("0 min");
  });
});
