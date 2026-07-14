import { describe, expect, it } from "vitest";
import type { GraphStructureResponse, TwinConceptStateResponse } from "./api";
import { buildConceptMap } from "./conceptMap";

// a → b → c, plus a → d (so d is a second frontier once a is held).
function structure(): GraphStructureResponse {
  return {
    nodes: [
      { conceptId: "a", slug: "a", title: "A", difficulty: 0 },
      { conceptId: "b", slug: "b", title: "B", difficulty: 0 },
      { conceptId: "c", slug: "c", title: "C", difficulty: 0 },
      { conceptId: "d", slug: "d", title: "D", difficulty: 0 },
    ],
    edges: [
      { from: "a", to: "b" },
      { from: "b", to: "c" },
      { from: "a", to: "d" },
    ],
  };
}

function state(conceptId: string, s: string, retrievability = 1): TwinConceptStateResponse {
  return { conceptId, state: s, pKnows: 0.9, retrievability };
}

describe("buildConceptMap", () => {
  it("shades a touched concept by its Twin state and leaves the untouched unseen", () => {
    const map = buildConceptMap(structure(), [state("a", "held"), state("b", "inProgress")]);
    const at = (id: string) => map.nodes.find((n) => n.conceptId === id)!;

    expect(at("a").state).toBe("held");
    expect(at("b").state).toBe("inProgress");
    expect(at("c").state).toBe("unseen"); // absent from the states feed → unseen, not a fake zero
    expect(at("d").state).toBe("unseen");
  });

  it("puts a not-held concept on the frontier only when every prerequisite is held", () => {
    // a is held; b (needs a) and d (needs a) become reachable. c (needs b) does not.
    const map = buildConceptMap(structure(), [state("a", "held")]);
    const at = (id: string) => map.nodes.find((n) => n.conceptId === id)!;

    expect(at("b").isFrontier).toBe(true);
    expect(at("d").isFrontier).toBe(true);
    expect(at("c").isFrontier).toBe(false); // its prerequisite b is not held
    expect(at("a").isFrontier).toBe(false); // already held — not a starting point
    expect(map.frontierCount).toBe(2);
  });

  it("treats a source concept as frontier from the very start", () => {
    const map = buildConceptMap(structure(), []); // nothing held yet
    const at = (id: string) => map.nodes.find((n) => n.conceptId === id)!;

    expect(at("a").isFrontier).toBe(true); // no prerequisites, not held → start here
    expect(at("b").isFrontier).toBe(false);
  });

  it("counts a decaying or lapsed prerequisite as still satisfied — it was once held", () => {
    // a lapsed; b's prerequisite is still structurally met (they did prove a), so b is reachable.
    const map = buildConceptMap(structure(), [state("a", "lapsed", 0.4)]);
    const at = (id: string) => map.nodes.find((n) => n.conceptId === id)!;

    expect(at("b").isFrontier).toBe(true);
  });

  it("tallies the states and writes an honest one-line summary", () => {
    const map = buildConceptMap(structure(), [
      state("a", "held"),
      state("b", "decaying", 0.8),
      state("c", "lapsed", 0.4),
    ]);

    expect(map.counts).toEqual({ held: 1, inProgress: 0, decaying: 1, lapsed: 1, unseen: 1 });
    expect(map.summary).toContain("4 concepts");
    expect(map.summary).toContain("1 held");
    expect(map.summary).toContain("1 decaying");
    expect(map.summary).toContain("1 lapsed");
    expect(map.summary).toContain("1 not started");
  });

  it("carries the layout through, so every node has a column and a row", () => {
    const map = buildConceptMap(structure(), []);
    expect(map.layerCount).toBeGreaterThan(0);
    for (const n of map.nodes) {
      expect(n.layer).toBeGreaterThanOrEqual(0);
      expect(n.row).toBeGreaterThanOrEqual(0);
    }
  });

  it("does not choke on an unknown state string — it falls back to unseen", () => {
    const map = buildConceptMap(structure(), [state("a", "quantum-superposition")]);
    expect(map.nodes.find((n) => n.conceptId === "a")!.state).toBe("unseen");
  });
});
