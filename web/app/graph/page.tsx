"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { AppShell } from "@/components/AppShell";
import { twin, type DecayForecastEntry, type TwinSummaryResponse } from "@/lib/api";
import { calibrationVerdict, forecastUrgency, STATES } from "@/lib/graph";
import { ConceptMap } from "./ConceptMap";

/*
  The Graph (prototype §6). The learner's own mind, with the decay showing. The node-link map leads —
  concepts and prerequisites, shaded by belief, the frontier ringed — and below it the honest tally:
  what is held, what is building, what is rotting, what has fallen below the line, and the decay
  forecast (the rep queue in all but name). The one thing PRAXIS lets you enjoy is the frontier
  advancing, and now you can watch it.
*/

export default function GraphPage() {
  const [summary, setSummary] = useState<TwinSummaryResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let active = true;
    void (async () => {
      const res = await twin.summary(30);
      if (!active) return;
      setLoading(false);
      if (!res.ok) {
        setError(res.message);
        return;
      }
      setError(null);
      setSummary(res.data);
    })();
    return () => {
      active = false;
    };
  }, [reloadKey]);

  return (
    <AppShell crumb="The Graph" current="The Graph">
      <div className="mx-auto max-w-3xl">
        <p className="text-[11px] uppercase tracking-[0.18em] text-ink-3">Your mind, on evidence</p>
        <h1 className="serif mt-2 text-[clamp(22px,2.6vw,30px)] font-normal leading-tight text-ink">
          What you hold — and what is quietly rotting.
        </h1>
        <p className="mt-3 max-w-prose text-[14px] leading-relaxed text-ink-2">
          Every number here moved on evidence, not on time spent. PRAXIS shows the one thing other
          apps hide: how much of what you know is slipping, and what you only think you still know.
        </p>

        <section className="mt-6" aria-label="Concept map">
          <ConceptMap />
        </section>

        <div className="mt-8 border-t border-line-soft pt-7">
          {loading ? (
            <p className="text-[13.5px] text-ink-3">Reading your Twin…</p>
          ) : error ? (
            <ErrorPanel message={error} onRetry={() => setReloadKey((k) => k + 1)} />
          ) : summary ? (
            <Summary summary={summary} />
          ) : null}
        </div>
      </div>
    </AppShell>
  );
}

function Summary({ summary }: { summary: TwinSummaryResponse }) {
  const total =
    summary.conceptsHeld + summary.conceptsInProgress + summary.conceptsLapsed;

  if (total === 0) {
    return (
      <div className="rounded-lg border border-line bg-surface px-4 py-6 text-center">
        <p className="text-[13.5px] text-ink-2">
          Nothing in your Twin yet — it is seeded by the Diagnostic, then it grows on evidence.
        </p>
        <Link
          href="/onboarding"
          className="mt-3 inline-flex rounded-md border border-line px-4 py-2 text-[13px] text-ink-2 hover:bg-surface-2"
        >
          Start onboarding →
        </Link>
      </div>
    );
  }

  return (
    <>
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {STATES.map((s) => (
          <div key={s.key} className="rounded-lg border border-line bg-surface p-4">
            <div className={`mono text-[26px] ${s.tone}`}>{s.count(summary)}</div>
            <div className="mt-1 text-[11px] font-medium uppercase tracking-[0.12em] text-ink-3">
              {s.label}
            </div>
            <p className="mt-1.5 text-[11.5px] leading-relaxed text-ink-4">{s.gloss}</p>
          </div>
        ))}
      </div>

      <section className="mt-7 rounded-lg border border-line bg-surface p-5">
        <div className="flex items-baseline justify-between">
          <h2 className="text-[11px] uppercase tracking-[0.14em] text-ink-3">Calibration</h2>
          <span className="mono text-[12px] text-ink-4">Brier {summary.overallBrierScore.toFixed(2)}</span>
        </div>
        <p className="mt-2 text-[13.5px] leading-relaxed text-ink-2">{calibrationVerdict(summary)}</p>
        {summary.activeMisconceptions > 0 ? (
          <p className="mt-2 text-[12px] text-critical">
            {summary.activeMisconceptions} active misconception
            {summary.activeMisconceptions === 1 ? "" : "s"} — named wrong beliefs the Graph is hunting.
          </p>
        ) : null}
      </section>

      <section className="mt-6">
        <div className="flex items-baseline justify-between">
          <h2 className="text-[11px] uppercase tracking-[0.14em] text-ink-3">
            Decaying soonest — your reps, in effect
          </h2>
          <span className="text-[11px] text-ink-4">next 30 days</span>
        </div>
        {summary.decayForecast.length > 0 ? (
          <ul className="mt-2 flex flex-col gap-1.5">
            {summary.decayForecast.map((e) => (
              <ForecastRow key={e.conceptId} entry={e} />
            ))}
          </ul>
        ) : (
          <p className="mt-2 rounded-md border border-line bg-surface px-4 py-3 text-[13px] text-good">
            Nothing is due to rot in the next 30 days. The frontier is holding.
          </p>
        )}
      </section>
    </>
  );
}

function ForecastRow({ entry }: { entry: DecayForecastEntry }) {
  const urgency = forecastUrgency(entry);
  const tone =
    urgency === "lapsed"
      ? "border-critical/45 text-critical"
      : urgency === "soon"
        ? "border-warn/45 text-warn"
        : "border-line text-ink-3";
  const when = entry.alreadyLapsed
    ? "already lapsed"
    : entry.daysUntilDue <= 1
      ? "due today"
      : `in ${Math.round(entry.daysUntilDue)} days`;

  return (
    <li className={`flex items-baseline justify-between gap-3 rounded-md border bg-surface px-3.5 py-2 ${tone}`}>
      <span className="mono text-[12px] text-ink-2">{entry.conceptId.slice(0, 8)}</span>
      <span className="flex items-baseline gap-3 text-[11.5px]">
        <span className="text-ink-4">
          P<span className="mono"> {entry.pKnows.toFixed(2)}</span>
        </span>
        <span className="text-ink-4">
          R<span className="mono"> {entry.retrievability.toFixed(2)}</span>
        </span>
        <span className="min-w-[72px] text-right">{when}</span>
      </span>
    </li>
  );
}

function ErrorPanel({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div
      role="alert"
      className="flex flex-col gap-3 rounded-md border border-serious/45 bg-surface px-4 py-4 sm:flex-row sm:items-center sm:justify-between"
    >
      <p className="text-[13.5px] text-ink-2">{message}</p>
      <button
        type="button"
        onClick={onRetry}
        className="shrink-0 rounded-md border border-line px-3 py-1.5 text-[12.5px] text-ink-2 hover:bg-surface-2"
      >
        Try again
      </button>
    </div>
  );
}
