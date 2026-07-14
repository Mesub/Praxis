/*
  The map model, before it is a map.

  Two feeds meet here: the graph's *structure* (every concept, every prerequisite arrow) and the
  Twin's *belief* (which concepts the learner holds, which are slipping, which have lapsed). This
  turns the pair into one positioned, shaded model — where each node sits, what state it is in, and
  whether it is on the frontier: the honest, single "one dopamine we permit" — a concept a learner
  can start *today*, because everything it needs is already held.

  It is pure. The colours and the SVG are the component's job; the decisions — what state a node is
  in, whether it is reachable — are made here, once, so they can be tested without a browser.
*/

import { layoutDag, type DagEdge, type DagNode, type PositionedNode } from "./dag";
import type { GraphStructureResponse, TwinConceptStateResponse } from "./api";

export type MapNodeState = "held" | "inProgress" | "decaying" | "lapsed" | "unseen";

export type MapNode = PositionedNode & {
  state: MapNodeState;
  /** True when the learner could start this today: not yet held, and every prerequisite *is* held. */
  isFrontier: boolean;
  /** The Twin's read-time retrievability, or null for a concept never touched. */
  retrievability: number | null;
};

export type MapModel = {
  nodes: MapNode[];
  edges: DagEdge[];
  layerCount: number;
  rowsPerLayer: number[];
  counts: Record<MapNodeState, number>;
  frontierCount: number;
  /** One honest sentence — the headline and the screen-reader summary of the whole map. */
  summary: string;
};

/** A concept counts as *held* — a satisfied prerequisite — whether it is fresh, decaying or lapsed:
 *  all three mean the learner did once prove it (the Twin's `IsHeld` is true for all of them). */
function wasHeld(state: MapNodeState): boolean {
  return state === "held" || state === "decaying" || state === "lapsed";
}

const KNOWN: readonly MapNodeState[] = ["held", "inProgress", "decaying", "lapsed", "unseen"];

function asState(raw: string | undefined): MapNodeState {
  return raw !== undefined && (KNOWN as readonly string[]).includes(raw)
    ? (raw as MapNodeState)
    : "unseen";
}

export function buildConceptMap(
  structure: GraphStructureResponse,
  states: readonly TwinConceptStateResponse[],
): MapModel {
  const dagNodes: DagNode[] = structure.nodes.map((n) => ({
    conceptId: n.conceptId,
    slug: n.slug,
    title: n.title,
    difficulty: n.difficulty,
  }));

  const layout = layoutDag(dagNodes, structure.edges);

  const stateById = new Map(states.map((s) => [s.conceptId, s] as const));

  // Prerequisites of each concept, from the edges (from → to means "from is required by to").
  const prereqs = new Map<string, string[]>();
  for (const n of structure.nodes) prereqs.set(n.conceptId, []);
  for (const e of structure.edges) {
    if (prereqs.has(e.to)) prereqs.get(e.to)!.push(e.from);
  }

  const resolvedState = new Map<string, MapNodeState>(
    structure.nodes.map((n) => [n.conceptId, asState(stateById.get(n.conceptId)?.state)] as const),
  );

  const nodes: MapNode[] = layout.nodes.map((n) => {
    const state = resolvedState.get(n.conceptId)!;
    const ps = prereqs.get(n.conceptId) ?? [];

    // The frontier: not held yet, and everything it needs already is. A source concept (no
    // prerequisites) that is not held is on the frontier by the same rule — nothing blocks it.
    const isFrontier =
      !wasHeld(state) && ps.every((p) => wasHeld(resolvedState.get(p) ?? "unseen"));

    return {
      ...n,
      state,
      isFrontier,
      retrievability: stateById.get(n.conceptId)?.retrievability ?? null,
    };
  });

  const counts: Record<MapNodeState, number> = {
    held: 0,
    inProgress: 0,
    decaying: 0,
    lapsed: 0,
    unseen: 0,
  };
  for (const n of nodes) counts[n.state]++;
  const frontierCount = nodes.filter((n) => n.isFrontier).length;

  return {
    nodes,
    edges: structure.edges,
    layerCount: layout.layerCount,
    rowsPerLayer: layout.rowsPerLayer,
    counts,
    frontierCount,
    summary: summarise(nodes.length, counts, frontierCount),
  };
}

function summarise(total: number, counts: Record<MapNodeState, number>, frontier: number): string {
  const parts: string[] = [];
  if (counts.held) parts.push(`${counts.held} held`);
  if (counts.decaying) parts.push(`${counts.decaying} decaying`);
  if (counts.lapsed) parts.push(`${counts.lapsed} lapsed`);
  if (counts.inProgress) parts.push(`${counts.inProgress} in progress`);
  if (counts.unseen) parts.push(`${counts.unseen} not started`);

  const breakdown = parts.length > 0 ? parts.join(", ") : "nothing yet";
  const frontierNote =
    frontier === 1 ? " 1 concept is ready to start now." : ` ${frontier} concepts are ready to start now.`;

  return `${total} concepts: ${breakdown}.${frontier > 0 ? frontierNote : ""}`;
}
