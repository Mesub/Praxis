/*
  The layout, and only the layout.

  A dependency graph is a picture waiting to be drawn wrong. The honest drawing puts prerequisites
  to the left of what they unlock, so the eye reads the map the way a learner walks it — left to
  right, nothing before the thing it needs. This module turns a set of concepts and their REQUIRES
  edges into an (x, y) for each, and does nothing else: no colours, no learner state, no clock, no
  randomness. It is a pure function, so it is testable, and so the same graph always draws the same —
  a map that jumped around on every render would be a map nobody could learn to trust.

  It assumes a DAG (the pack validator refuses a cycle, and this is fed by the validated pack), but it
  is guarded against one anyway — a stray cycle degrades the layout, it does not hang the browser.
*/

export type DagNode = {
  conceptId: string;
  slug: string;
  title: string;
  difficulty: number;
};

/** A prerequisite edge: `from` must be held before `to`. */
export type DagEdge = { from: string; to: string };

export type PositionedNode = DagNode & {
  /** Column: the longest chain of prerequisites behind it. Sources are 0; the objective is deepest. */
  layer: number;
  /** Row within the column, 0-based and contiguous. */
  row: number;
};

export type DagLayout = {
  nodes: PositionedNode[];
  /** Number of columns; the objective sits in the last one. */
  layerCount: number;
  /** How many nodes stand in each column — the tallest column sets the canvas height. */
  rowsPerLayer: number[];
};

/**
 * **Longest-path layering, then barycentre ordering** — the two cheap halves of a readable DAG.
 *
 * The *column* of a concept is the longest chain of prerequisites behind it, so every edge points
 * strictly forward (a prerequisite is always left of what it unlocks) and nothing floats past the
 * thing that needs it. The *row* within a column is chosen to sit each concept near the average
 * height of its prerequisites — a one-pass barycentre that pulls related things level and thins out
 * crossing lines — with the slug as a deterministic tie-break so the map never wobbles between
 * renders.
 */
export function layoutDag(nodes: readonly DagNode[], edges: readonly DagEdge[]): DagLayout {
  const byId = new Map(nodes.map((n) => [n.conceptId, n] as const));

  const prereqs = new Map<string, string[]>();
  for (const n of nodes) prereqs.set(n.conceptId, []);
  for (const e of edges) {
    // Only edges between concepts we actually hold — an arrow to nowhere is not drawn.
    if (byId.has(e.from) && byId.has(e.to)) {
      prereqs.get(e.to)!.push(e.from);
    }
  }

  const layer = layerOf(nodes, prereqs);

  // Group into columns.
  const layerCount = nodes.length === 0 ? 0 : Math.max(...nodes.map((n) => layer.get(n.conceptId)!)) + 1;
  const columns: DagNode[][] = Array.from({ length: layerCount }, () => []);
  for (const n of nodes) columns[layer.get(n.conceptId)!].push(n);

  // Order each column left-to-right; every node's prerequisites live in earlier columns and so are
  // already placed, which is what makes a single barycentre pass enough.
  const row = new Map<string, number>();
  for (let l = 0; l < layerCount; l++) {
    const col = columns[l];
    const keyed = col.map((n) => ({
      node: n,
      // Average row of this node's prerequisites; an isolated node keeps its slug order.
      bary: barycentre(prereqs.get(n.conceptId)!, row),
    }));

    keyed.sort((a, b) => {
      if (a.bary !== b.bary) return a.bary - b.bary;
      return a.node.slug < b.node.slug ? -1 : a.node.slug > b.node.slug ? 1 : 0;
    });

    keyed.forEach((k, i) => row.set(k.node.conceptId, i));
  }

  const positioned: PositionedNode[] = nodes.map((n) => ({
    ...n,
    layer: layer.get(n.conceptId)!,
    row: row.get(n.conceptId)!,
  }));

  return {
    nodes: positioned,
    layerCount,
    rowsPerLayer: columns.map((c) => c.length),
  };
}

/** The longest chain of prerequisites behind each node. Memoised, with a cycle guard so a malformed
 *  graph degrades rather than recurses forever. */
function layerOf(nodes: readonly DagNode[], prereqs: Map<string, string[]>): Map<string, number> {
  const memo = new Map<string, number>();
  const visiting = new Set<string>();

  const depth = (id: string): number => {
    const cached = memo.get(id);
    if (cached !== undefined) return cached;
    if (visiting.has(id)) return 0; // a cycle — stop, do not hang; the pack forbids this anyway.

    visiting.add(id);
    const ps = prereqs.get(id) ?? [];
    const d = ps.length === 0 ? 0 : Math.max(...ps.map(depth)) + 1;
    visiting.delete(id);

    memo.set(id, d);
    return d;
  };

  for (const n of nodes) depth(n.conceptId);
  return memo;
}

/** Mean assigned row of a node's prerequisites; -1 when it has none, so isolated nodes sort first and
 *  fall back to slug order. */
function barycentre(prereqIds: readonly string[], row: Map<string, number>): number {
  const rows = prereqIds.map((p) => row.get(p)).filter((r): r is number => r !== undefined);
  if (rows.length === 0) return -1;
  return rows.reduce((a, b) => a + b, 0) / rows.length;
}
