"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { AppShell } from "@/components/AppShell";
import { memory, type RepResponse, type RepsResponse } from "@/lib/api";
import { minutesLabel, retrievabilityLabel, urgencyOf } from "@/lib/reps";

/*
  Reps (prototype §7). A queue ordered by what a rep is worth, not by what you owe. There is no debt
  count and no snooze — the number of things you are "behind on" is the one PRAXIS refuses to show,
  so there is nothing to bury. An empty queue is a good day, said plainly. And a concept that decayed
  past saving is named honestly: a re-learn, not a quick review that would fail.
*/

export default function RepsPage() {
  const [data, setData] = useState<RepsResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let active = true;
    void (async () => {
      const res = await memory.reps();
      if (!active) return;
      setLoading(false);
      if (!res.ok) {
        setError(res.message);
        return;
      }
      setError(null);
      setData(res.data);
    })();
    return () => {
      active = false;
    };
  }, [reloadKey]);

  return (
    <AppShell crumb="Reps" current="Reps">
      <div className="mx-auto max-w-2xl">
        <p className="text-[11px] uppercase tracking-[0.18em] text-ink-3">Retrieval, when it bites</p>
        <h1 className="serif mt-2 text-[clamp(22px,2.6vw,30px)] font-normal leading-tight text-ink">
          Worth reviewing — not what you owe.
        </h1>
        <p className="mt-3 max-w-prose text-[14px] leading-relaxed text-ink-2">
          Ordered by what each rep is <i>worth</i>, soonest-to-rot first. There is no count of what you
          are behind on, because being behind is not a number PRAXIS will make you feel.
        </p>

        <div className="mt-6">
          {loading ? (
            <p className="text-[13.5px] text-ink-3">Finding the reps that will bite…</p>
          ) : error ? (
            <ErrorPanel message={error} onRetry={() => setReloadKey((k) => k + 1)} />
          ) : data ? (
            <Queue data={data} />
          ) : null}
        </div>
      </div>
    </AppShell>
  );
}

function Queue({ data }: { data: RepsResponse }) {
  if (data.nothingWorthDoing && data.due.length === 0) {
    return (
      <div className="rounded-lg border border-good/40 bg-surface px-4 py-6 text-center">
        <p className="text-[14px] text-ink">Nothing is worth reviewing right now.</p>
        <p className="mt-1.5 text-[12.5px] text-ink-3">
          Everything you hold is still sharp. Repping it early buys almost nothing — being early is
          not a virtue. Come back when something has faded enough to bite.
        </p>
      </div>
    );
  }

  return (
    <>
      {data.due.length > 0 ? (
        <>
          <div className="flex items-baseline justify-between">
            <h2 className="text-[11px] uppercase tracking-[0.14em] text-ink-3">Due, by worth</h2>
            <span className="text-[11px] text-ink-4">
              ~{minutesLabel(data.expectedMinutes)} to clear
            </span>
          </div>
          <ul className="mt-2 flex flex-col gap-2">
            {data.due.map((rep) => (
              <RepRow key={rep.conceptId} rep={rep} />
            ))}
          </ul>
        </>
      ) : null}

      {data.lost.length > 0 ? (
        <section className="mt-7">
          <h2 className="text-[11px] uppercase tracking-[0.14em] text-critical">
            Past saving — a re-learn, not a review
          </h2>
          {data.aboutWhatYouLost ? (
            <p className="mt-2 text-[12.5px] leading-relaxed text-ink-3">{data.aboutWhatYouLost}</p>
          ) : null}
          <ul className="mt-2 flex flex-col gap-1.5">
            {data.lost.map((rep) => (
              <li
                key={rep.conceptId}
                className="flex items-baseline justify-between rounded-md border border-critical/35 bg-surface px-3.5 py-2"
              >
                <span className="text-[13px] text-ink-2">{rep.slug}</span>
                <span className="mono text-[11px] text-ink-4">
                  {retrievabilityLabel(rep.retrievability)}
                </span>
              </li>
            ))}
          </ul>
        </section>
      ) : null}
    </>
  );
}

function RepRow({ rep }: { rep: RepResponse }) {
  const u = urgencyOf(rep.urgency);
  return (
    <li className="rounded-lg border border-line bg-surface p-4">
      <div className="flex items-baseline justify-between gap-3">
        <div className="flex items-baseline gap-2">
          <span className={`text-[10.5px] font-medium uppercase tracking-[0.12em] ${u.tone}`}>
            {u.label}
          </span>
          <span className="text-[14px] text-ink">{rep.slug}</span>
        </div>
        <span className="mono text-[12px] text-ink-4">
          {retrievabilityLabel(rep.retrievability)} · {minutesLabel(rep.expectedMinutes)}
        </span>
      </div>
      <p className="mt-2 text-[12.5px] leading-relaxed text-ink-3">{rep.because}</p>
      <Link
        href={`/session?concept=${rep.conceptId}`}
        className="mt-3 inline-flex rounded-md border border-line px-3.5 py-1.5 text-[12.5px] text-ink-2 hover:bg-surface-2"
      >
        Review it →
      </Link>
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
