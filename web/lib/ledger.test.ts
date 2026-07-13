import { describe, expect, it } from "vitest";
import type { CompetencyRecordResponse } from "./api";
import { assuranceLabel, exportFilename, issuedLabel, methodOf } from "./ledger";

describe("methodOf", () => {
  it("marks execution / reality / key-match as unfakeable, and rubric as not", () => {
    expect(methodOf("execution").unfakeable).toBe(true);
    expect(methodOf("reality").unfakeable).toBe(true);
    expect(methodOf("keyMatch").unfakeable).toBe(true);
    expect(methodOf("rubric").unfakeable).toBe(false);
  });

  it("names the rubric method as capped, not certain", () => {
    expect(methodOf("rubric").label.toLowerCase()).toContain("capped");
  });

  it("is case-insensitive and degrades gracefully", () => {
    expect(methodOf("EXECUTION").unfakeable).toBe(true);
    expect(methodOf("mystery").label).toBe("mystery");
  });
});

describe("labels", () => {
  it("renders assurance as a whole percent", () => {
    expect(assuranceLabel(0.85)).toBe("85%");
  });

  it("formats a valid issued date and passes through a bad one", () => {
    expect(issuedLabel("2026-01-01T00:00:00Z")).toMatch(/2026/);
    expect(issuedLabel("not-a-date")).toBe("not-a-date");
  });
});

describe("exportFilename", () => {
  it("is stable and dated", () => {
    const record = { issuedAt: "2026-03-15T12:00:00Z" } as CompetencyRecordResponse;
    expect(exportFilename(record)).toBe("praxis-competency-record-2026-03-15.json");
  });
});
