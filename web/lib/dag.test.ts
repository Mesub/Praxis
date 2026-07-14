import { describe, expect, it } from "vitest";
import { layoutDag, type DagEdge, type DagNode } from "./dag";

function node(id: string, difficulty = 0): DagNode {
  return { conceptId: id, slug: id, title: id.toUpperCase(), difficulty };
}

function edge(from: string, to: string): DagEdge {
  return { from, to };
}

describe("layoutDag", () => {
  it("lays a chain out one concept per column, all in the same row", () => {
    const layout = layoutDag(
      [node("a"), node("b"), node("c")],
      [edge("a", "b"), edge("b", "c")],
    );

    const at = (id: string) => layout.nodes.find((n) => n.conceptId === id)!;
    expect(at("a").layer).toBe(0);
    expect(at("b").layer).toBe(1);
    expect(at("c").layer).toBe(2);
    expect(layout.layerCount).toBe(3);
    expect(layout.rowsPerLayer).toEqual([1, 1, 1]);
  });

  it("puts the two middle concepts of a diamond side by side", () => {
    // a → b, a → c, b → d, c → d
    const layout = layoutDag(
      [node("a"), node("b"), node("c"), node("d")],
      [edge("a", "b"), edge("a", "c"), edge("b", "d"), edge("c", "d")],
    );

    const at = (id: string) => layout.nodes.find((n) => n.conceptId === id)!;
    expect(at("a").layer).toBe(0);
    expect(at("b").layer).toBe(1);
    expect(at("c").layer).toBe(1);
    expect(at("d").layer).toBe(2); // the longest path a→b→d (or a→c→d) is length 2
    expect(new Set([at("b").row, at("c").row])).toEqual(new Set([0, 1]));
  });

  it("places a concept past its DEEPEST prerequisite, not its shallowest", () => {
    // d requires both a (a source) and c (which is itself two deep): a→c long path, a→d and c→d.
    const layout = layoutDag(
      [node("a"), node("b"), node("c"), node("d")],
      [edge("a", "b"), edge("b", "c"), edge("a", "d"), edge("c", "d")],
    );
    const at = (id: string) => layout.nodes.find((n) => n.conceptId === id)!;

    // c is at layer 2 (a→b→c); d requires c, so it must sit *after* c, at layer 3 — not layer 1
    // just because it also requires the source a.
    expect(at("c").layer).toBe(2);
    expect(at("d").layer).toBe(3);
  });

  it("makes every edge point strictly forward — no arrow ever goes backward or sideways", () => {
    const nodes = [node("a"), node("b"), node("c"), node("d"), node("e")];
    const edges = [edge("a", "b"), edge("a", "c"), edge("b", "d"), edge("c", "d"), edge("d", "e")];
    const layout = layoutDag(nodes, edges);
    const at = (id: string) => layout.nodes.find((n) => n.conceptId === id)!;

    for (const e of edges) {
      expect(at(e.to).layer).toBeGreaterThan(at(e.from).layer);
    }
  });

  it("puts the source in the first column and the sink in the last", () => {
    const nodes = [node("a"), node("b"), node("c"), node("d"), node("e")];
    const edges = [edge("a", "b"), edge("a", "c"), edge("b", "d"), edge("c", "d"), edge("d", "e")];
    const layout = layoutDag(nodes, edges);
    const at = (id: string) => layout.nodes.find((n) => n.conceptId === id)!;

    expect(at("a").layer).toBe(0); // nothing points to it
    expect(at("e").layer).toBe(layout.layerCount - 1); // it points to nothing
  });

  it("gives contiguous 0-based rows within every column", () => {
    const layout = layoutDag(
      [node("a"), node("b"), node("c"), node("d"), node("e")],
      [edge("a", "c"), edge("b", "c"), edge("a", "d"), edge("b", "d"), edge("c", "e"), edge("d", "e")],
    );

    for (let l = 0; l < layout.layerCount; l++) {
      const rows = layout.nodes.filter((n) => n.layer === l).map((n) => n.row).sort((x, y) => x - y);
      expect(rows).toEqual(rows.map((_, i) => i));
    }
  });

  it("is deterministic — the same graph always draws the same map", () => {
    const nodes = [node("z"), node("a"), node("m"), node("b")];
    const edges = [edge("a", "m"), edge("z", "m"), edge("m", "b")];

    const once = layoutDag(nodes, edges);
    const twice = layoutDag(nodes, edges);

    expect(twice).toEqual(once);
  });

  it("does not hang on a stray cycle — it degrades, it does not recurse forever", () => {
    // The pack forbids this, but a client must never freeze on bad input.
    const layout = layoutDag(
      [node("a"), node("b")],
      [edge("a", "b"), edge("b", "a")],
    );

    expect(layout.nodes).toHaveLength(2);
  });

  it("handles an empty graph without inventing columns", () => {
    const layout = layoutDag([], []);
    expect(layout.layerCount).toBe(0);
    expect(layout.nodes).toEqual([]);
  });
});
