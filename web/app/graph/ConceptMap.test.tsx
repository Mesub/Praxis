import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import type { ApiResult, GraphStructureResponse, TwinConceptStateResponse } from "@/lib/api";

const structure = vi.fn();
const states = vi.fn();
vi.mock("@/lib/api", () => ({
  graph: { structure: (...a: unknown[]) => structure(...a) },
  twin: { states: (...a: unknown[]) => states(...a) },
}));

import { ConceptMap } from "./ConceptMap";

function ok<T>(data: T): ApiResult<T> {
  return { ok: true, data };
}

const STRUCT: GraphStructureResponse = {
  nodes: [
    { conceptId: "a", slug: "tokenisation", title: "Tokenisation", difficulty: 0 },
    { conceptId: "b", slug: "the-inverted-index", title: "The inverted index", difficulty: 0 },
    { conceptId: "c", slug: "bm25", title: "BM25", difficulty: 0 },
  ],
  edges: [
    { from: "a", to: "b" },
    { from: "b", to: "c" },
  ],
};

function held(id: string): TwinConceptStateResponse {
  return { conceptId: id, state: "held", pKnows: 0.9, retrievability: 1 };
}

describe("ConceptMap", () => {
  beforeEach(() => {
    structure.mockReset();
    states.mockReset();
  });

  it("draws a named, stated node for every concept, with a legend and a summary", async () => {
    structure.mockResolvedValue(ok(STRUCT));
    states.mockResolvedValue(ok<TwinConceptStateResponse[]>([held("a")]));
    render(<ConceptMap />);

    // A node carries its name AND its state — colour is never the only signal.
    expect(await screen.findByRole("listitem", { name: /tokenisation — held/i })).toBeInTheDocument();
    // The legend maps colour to meaning.
    expect(screen.getByText("Held")).toBeInTheDocument();
    expect(screen.getByText("Not started")).toBeInTheDocument();
    // The one-line summary (also the map's accessible name).
    expect(screen.getByText(/3 concepts:/i)).toBeInTheDocument();
  });

  it("rings the frontier — a not-held concept whose every prerequisite is held", async () => {
    structure.mockResolvedValue(ok(STRUCT));
    states.mockResolvedValue(ok<TwinConceptStateResponse[]>([held("a")]));
    render(<ConceptMap />);

    // b requires a, which is held → ready to start; c (requires b) is not.
    expect(
      await screen.findByRole("listitem", { name: /the inverted index.*ready to start/i }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("listitem", { name: /bm25.*ready to start/i }),
    ).not.toBeInTheDocument();
  });

  it("still draws the terrain when belief cannot be read — every node simply unseen", async () => {
    structure.mockResolvedValue(ok(STRUCT));
    states.mockResolvedValue({ ok: false, message: "down", errors: [] } satisfies ApiResult<TwinConceptStateResponse[]>);
    render(<ConceptMap />);

    // The map does not vanish because the Twin hiccuped; it draws, unseen.
    expect(await screen.findByRole("listitem", { name: /bm25 — not started/i })).toBeInTheDocument();
  });

  it("shows an honest error when the structure itself cannot be read", async () => {
    structure.mockResolvedValue({
      ok: false,
      message: "PRAXIS could not be reached.",
      errors: [],
    } satisfies ApiResult<GraphStructureResponse>);
    states.mockResolvedValue(ok<TwinConceptStateResponse[]>([]));
    render(<ConceptMap />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/could not be reached/i);
  });

  it("says so plainly when there is no map to draw", async () => {
    structure.mockResolvedValue(ok<GraphStructureResponse>({ nodes: [], edges: [] }));
    states.mockResolvedValue(ok<TwinConceptStateResponse[]>([]));
    render(<ConceptMap />);

    expect(await screen.findByText(/no map to draw yet/i)).toBeInTheDocument();
  });
});
