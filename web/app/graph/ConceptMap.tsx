"use client";

import { useEffect, useState } from "react";
import { graph, twin } from "@/lib/api";
import { buildConceptMap, type MapModel, type MapNode, type MapNodeState } from "@/lib/conceptMap";

/*
  The map (prototype §6, the follow-up the state list promised). Concepts as nodes, prerequisites as
  arrows, each node shaded by what the Twin believes — held, slipping, lapsed, in progress, or not yet
  started — with the frontier ringed: the concepts a learner could start today. Colour is never the
  only signal (every node carries its name, there is a legend, and the whole map has a one-line
  summary a screen reader reads), and there is no motion to disorient anyone. The one dopamine PRAXIS
  permits is watching the frontier advance; this is where you watch it.
*/

const NODE_W = 152;
const NODE_H = 34;
const COL_GAP = 200;
const ROW_GAP = 58;
const PAD = 26;

type Shade = { label: string; color: string; filled: boolean };

const SHADE: Record<MapNodeState, Shade> = {
  held: { label: "Held", color: "var(--color-good)", filled: true },
  inProgress: { label: "In progress", color: "var(--color-s1)", filled: true },
  decaying: { label: "Decaying", color: "var(--color-warn)", filled: true },
  lapsed: { label: "Lapsed", color: "var(--color-critical)", filled: true },
  unseen: { label: "Not started", color: "var(--color-ink-4)", filled: false },
};

const LEGEND_ORDER: MapNodeState[] = ["held", "inProgress", "decaying", "lapsed", "unseen"];

export function ConceptMap() {
  const [model, setModel] = useState<MapModel | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let active = true;
    void (async () => {
      // Structure is required to draw anything; belief is best-effort — if the Twin hiccups, the
      // terrain still draws, every node simply unseen, rather than the whole map vanishing.
      const [structureRes, statesRes] = await Promise.all([graph.structure(), twin.states()]);
      if (!active) return;
      setLoading(false);
      if (!structureRes.ok) {
        setError(structureRes.message);
        return;
      }
      setError(null);
      setModel(buildConceptMap(structureRes.data, statesRes.ok ? statesRes.data : []));
    })();
    return () => {
      active = false;
    };
  }, [reloadKey]);

  if (loading) {
    return <p className="text-[13.5px] text-ink-3">Drawing the map…</p>;
  }

  if (error) {
    return (
      <div
        role="alert"
        className="flex flex-col gap-3 rounded-md border border-serious/45 bg-surface px-4 py-4 sm:flex-row sm:items-center sm:justify-between"
      >
        <p className="text-[13.5px] text-ink-2">{error}</p>
        <button
          type="button"
          onClick={() => setReloadKey((k) => k + 1)}
          className="shrink-0 rounded-md border border-line px-3 py-1.5 text-[12.5px] text-ink-2 hover:bg-surface-2"
        >
          Try again
        </button>
      </div>
    );
  }

  if (!model || model.nodes.length === 0) {
    return (
      <p className="rounded-md border border-line bg-surface px-4 py-3 text-[13px] text-ink-3">
        There is no map to draw yet.
      </p>
    );
  }

  return <Diagram model={model} />;
}

function Diagram({ model }: { model: MapModel }) {
  const maxRows = Math.max(1, ...model.rowsPerLayer);
  const width = PAD * 2 + Math.max(0, model.layerCount - 1) * COL_GAP + NODE_W;
  const height = PAD * 2 + (maxRows - 1) * ROW_GAP + NODE_H;

  const pos = new Map<string, { x: number; y: number }>();
  for (const n of model.nodes) {
    const x = PAD + n.layer * COL_GAP;
    const offset = (maxRows - model.rowsPerLayer[n.layer]) / 2;
    const y = PAD + (n.row + offset) * ROW_GAP;
    pos.set(n.conceptId, { x, y });
  }

  return (
    <figure className="m-0">
      <figcaption className="text-[13px] leading-relaxed text-ink-2">{model.summary}</figcaption>

      <div className="mt-3 flex flex-wrap gap-x-4 gap-y-1.5 text-[11.5px]">
        {LEGEND_ORDER.map((s) => (
          <span key={s} className="inline-flex items-center gap-1.5">
            <span
              aria-hidden="true"
              className="inline-block h-2.5 w-2.5 rounded-[3px]"
              style={{
                backgroundColor: SHADE[s].filled ? SHADE[s].color : "transparent",
                boxShadow: `inset 0 0 0 1.5px ${SHADE[s].color}`,
              }}
            />
            <span className="text-ink-3">{SHADE[s].label}</span>
          </span>
        ))}
        <span className="inline-flex items-center gap-1.5">
          <span
            aria-hidden="true"
            className="inline-block h-2.5 w-2.5 rounded-[3px]"
            style={{ boxShadow: "0 0 0 2px var(--color-accent)" }}
          />
          <span className="text-ink-3">Ready to start</span>
        </span>
      </div>

      <div className="mt-4 overflow-x-auto rounded-lg border border-line bg-surface p-3">
        <svg
          width={width}
          height={height}
          viewBox={`0 0 ${width} ${height}`}
          role="group"
          aria-roledescription="concept map"
          aria-label={model.summary}
          className="block"
        >
          <g stroke="var(--color-line)" fill="none">
            {model.edges.map((e) => {
              const a = pos.get(e.from);
              const b = pos.get(e.to);
              if (!a || !b) return null;
              const x1 = a.x + NODE_W;
              const y1 = a.y + NODE_H / 2;
              const x2 = b.x;
              const y2 = b.y + NODE_H / 2;
              const dx = Math.max(24, (x2 - x1) * 0.5);
              return (
                <path
                  key={`${e.from}->${e.to}`}
                  d={`M${x1} ${y1} C ${x1 + dx} ${y1}, ${x2 - dx} ${y2}, ${x2} ${y2}`}
                  strokeWidth={1.25}
                  strokeOpacity={0.8}
                />
              );
            })}
          </g>

          <g role="list">
            {model.nodes.map((n) => (
              <Node key={n.conceptId} node={n} at={pos.get(n.conceptId)!} />
            ))}
          </g>
        </svg>
      </div>
    </figure>
  );
}

function Node({ node, at }: { node: MapNode; at: { x: number; y: number } }) {
  const shade = SHADE[node.state];
  const label = `${node.title} — ${shade.label.toLowerCase()}${node.isFrontier ? ", ready to start" : ""}`;

  return (
    <g role="listitem" tabIndex={0} aria-label={label} className="outline-offset-2">
      <title>{label}</title>
      {node.isFrontier ? (
        <rect
          x={at.x - 3}
          y={at.y - 3}
          width={NODE_W + 6}
          height={NODE_H + 6}
          rx={9}
          fill="none"
          stroke="var(--color-accent)"
          strokeWidth={2}
        />
      ) : null}
      <rect
        x={at.x}
        y={at.y}
        width={NODE_W}
        height={NODE_H}
        rx={7}
        fill={shade.color}
        fillOpacity={shade.filled ? 0.16 : 0}
        stroke={shade.color}
        strokeOpacity={shade.filled ? 1 : 0.55}
        strokeWidth={1.4}
      />
      <text
        x={at.x + 11}
        y={at.y + NODE_H / 2}
        dominantBaseline="central"
        fontSize={11}
        fill={node.state === "unseen" ? "var(--color-ink-4)" : "var(--color-ink)"}
      >
        {truncate(node.title)}
      </text>
    </g>
  );
}

function truncate(title: string, max = 20): string {
  return title.length > max ? `${title.slice(0, max - 1)}…` : title;
}
