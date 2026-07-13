"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { AppShell } from "@/components/AppShell";
import {
  scheduler,
  SESSION_LENGTHS,
  type SessionLength,
  type TodayCardResponse,
} from "@/lib/api";
import { actionLabel, presentationOf } from "@/lib/today";

/*
  Today (prototype §3). The whole console resolves to one question — "I have fifteen minutes, what do
  I do?" — and PRAXIS answers with one card, not a feed. The only choice offered is how long you
  have, and even that is four buttons, not a text box (a text box asking how long you have is just
  planning by another name). Everything that lost is kept, but behind an explicit "why this?" — the
  card is inspectable, never a menu.
*/

export default function TodayPage() {
  const [minutes, setMinutes] = useState<SessionLength>(15);
  const [reloadKey, setReloadKey] = useState(0);
  const [card, setCard] = useState<TodayCardResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  // The single fetch: on mount, on a session-length change, and on an explicit retry (reloadKey).
  // State is updated only after the await resolves — never synchronously inside the effect.
  useEffect(() => {
    let active = true;
    void (async () => {
      const res = await scheduler.today(minutes);
      if (!active) return;
      setLoading(false);
      if (!res.ok) {
        setCard(null);
        setError(res.message);
        return;
      }
      setError(null);
      setCard(res.data);
    })();
    return () => {
      active = false;
    };
  }, [minutes, reloadKey]);

  return (
    <AppShell crumb="Today" current="Today">
      <div className="mx-auto max-w-2xl">
        <div className="text-center">
          <p className="text-[11px] uppercase tracking-[0.18em] text-ink-3">
            One answer, not a feed
          </p>
          <h1 className="serif mt-2 text-[clamp(22px,2.6vw,28px)] font-normal text-ink-2">
            What do I do in the next {minutes} minutes?
          </h1>
        </div>

        <div className="mt-6 flex justify-center gap-2" role="group" aria-label="How long do you have?">
          {SESSION_LENGTHS.map((m) => {
            const active = m === minutes;
            return (
              <button
                key={m}
                type="button"
                aria-pressed={active}
                onClick={() => setMinutes(m)}
                className={`mono min-w-[52px] rounded-md border px-3 py-1.5 text-[13px] transition-[background-color,color,border-color] duration-[220ms] ease-brand ${
                  active
                    ? "border-accent-line bg-surface-2 text-ink"
                    : "border-line text-ink-3 hover:border-ink-4 hover:text-ink-2"
                }`}
              >
                {m}m
              </button>
            );
          })}
        </div>

        <div className="mt-6">
          {loading ? (
            <p className="text-center text-[13.5px] text-ink-3">
              Choosing the one thing worth your {minutes} minutes…
            </p>
          ) : error ? (
            <ErrorPanel message={error} onRetry={() => setReloadKey((k) => k + 1)} />
          ) : card ? (
            <TodayCard card={card} minutes={minutes} />
          ) : null}
        </div>
      </div>
    </AppShell>
  );
}

function TodayCard({ card, minutes }: { card: TodayCardResponse; minutes: SessionLength }) {
  const [showWhy, setShowWhy] = useState(false);
  const p = presentationOf(card.kind);
  const rest = card.kind === "Nothing";

  return (
    <section className="rounded-lg border border-line bg-surface p-6">
      <div className="flex items-center gap-2">
        <span className={`text-[11px] font-medium uppercase tracking-[0.14em] ${p.tone}`}>
          {p.label}
        </span>
        {!rest ? <span className="mono text-[11px] text-ink-4">· {card.minutes} min</span> : null}
      </div>

      <h2 className="serif mt-3 text-[24px] font-normal leading-snug text-ink">{card.headline}</h2>
      <p className="mt-2 text-[12.5px] text-ink-4">{p.gloss}</p>

      <p className="mt-4 rounded-md border border-line bg-bg px-4 py-3 text-[13.5px] leading-relaxed text-ink-2">
        <span className="font-medium text-ink">Why now: </span>
        {card.because}
      </p>

      <div className="mt-5">
        {rest ? (
          <p className="text-[14px] text-ink-2">{actionLabel(card.kind, minutes)}.</p>
        ) : (
          <Link
            href={`/session?concept=${card.conceptId}&minutes=${card.minutes}`}
            className="inline-flex items-center justify-center rounded-md bg-accent px-5 py-2.5 text-[13.5px] font-medium text-accent-ink transition-[opacity] duration-[220ms] ease-brand hover:opacity-95"
          >
            {actionLabel(card.kind, card.minutes)}
          </Link>
        )}
      </div>

      {card.alsoDue.length > 0 ? (
        <div className="mt-5 border-t border-line pt-4">
          <button
            type="button"
            aria-expanded={showWhy}
            onClick={() => setShowWhy((s) => !s)}
            className="text-[12.5px] text-ink-3 underline-offset-4 hover:text-ink-2 hover:underline"
          >
            {showWhy ? "Hide" : "Why this and not something else?"}
          </button>
          {showWhy ? (
            <div className="mt-3">
              <p className="text-[11.5px] text-ink-4">
                Everything else that was due today, and why it lost. This is the audit trail, not a
                menu — the choice was already made so you did not have to.
              </p>
              <ul className="mt-2 flex flex-col gap-1.5">
                {card.alsoDue.map((d) => (
                  <li
                    key={d.conceptId}
                    className="flex items-baseline justify-between gap-3 rounded border border-line bg-bg px-3 py-1.5"
                  >
                    <span className="text-[12.5px] text-ink-2">{d.slug}</span>
                    <span className="text-[11px] text-ink-4">
                      {d.reason} · <span className="mono">{d.minutes}m</span>
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
        </div>
      ) : null}
    </section>
  );
}

function ErrorPanel({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div
      role="alert"
      className="rounded-md border border-serious/45 bg-surface px-4 py-4 text-center"
    >
      <p className="text-[13.5px] text-ink-2">{message}</p>
      <div className="mt-3 flex justify-center gap-3">
        <button
          type="button"
          onClick={onRetry}
          className="rounded-md border border-line px-3 py-1.5 text-[12.5px] text-ink-2 hover:bg-surface-2"
        >
          Try again
        </button>
        <Link
          href="/onboarding"
          className="rounded-md border border-line px-3 py-1.5 text-[12.5px] text-ink-2 hover:bg-surface-2"
        >
          No Objective yet? Start here
        </Link>
      </div>
    </div>
  );
}
