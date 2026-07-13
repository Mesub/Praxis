import { describe, expect, it } from "vitest";
import type { CardKind } from "./api";
import { SESSION_LENGTHS } from "./api";
import { actionLabel, presentationOf } from "./today";

const ALL: CardKind[] = ["Rescue", "Rep", "Learn", "Refresh", "Nothing"];

describe("presentationOf", () => {
  it("has a presentation for every card kind", () => {
    for (const k of ALL) {
      expect(presentationOf(k).label.length).toBeGreaterThan(0);
    }
  });

  it("marks only Nothing as non-actionable — it is the honest rest state", () => {
    expect(presentationOf("Nothing").actionable).toBe(false);
    for (const k of ALL.filter((x) => x !== "Nothing")) {
      expect(presentationOf(k).actionable).toBe(true);
    }
  });
});

describe("actionLabel", () => {
  it("offers to start an actionable card with its minutes", () => {
    expect(actionLabel("Learn", 15)).toBe("Start — 15 minutes");
  });

  it("offers rest, not work, when nothing is due", () => {
    expect(actionLabel("Nothing", 15).toLowerCase()).toContain("rest");
  });
});

describe("the session lengths", () => {
  it("are the four the day actually has room for", () => {
    expect([...SESSION_LENGTHS]).toEqual([5, 10, 15, 25]);
  });
});
