"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { tutor, type SessionResponse } from "@/lib/api";
import {
  budgetFraction,
  isExhausted,
  rungBlurb,
  toRungParam,
} from "@/lib/session";

/*
  The Session (prototype §4). A Socratic dialogue in which the tutor never gives the answer — it has
  none to give — and instead offers a ladder of hints, each deeper one costing more of a budget the
  learner watches drain. The budget is the star of this screen, on purpose: a scarcity you cannot see
  is an ambush, not a lesson. When it can no longer buy the next rung, the session does not fail and
  does not surrender the answer; it asks where the reasoning breaks and reschedules around the reply.

  Streaming hints (SSE) and production earn-back are deferred to the second half — this screen is the
  budget ticking down.
*/

const NIL_GUID = "00000000-0000-0000-0000-000000000000";

function initialParam(name: string, fallback: string): string {
  if (typeof window === "undefined") return fallback;
  return new URLSearchParams(window.location.search).get(name) ?? fallback;
}

type Line = { who: "tutor" | "you"; text: string; rung?: string; cost?: number };

export default function SessionPage() {
  const [concept] = useState(() => initialParam("concept", NIL_GUID));
  const [minutes] = useState(() => Number(initialParam("minutes", "15")) || 15);

  const [session, setSession] = useState<SessionResponse | null>(null);
  const [starting, setStarting] = useState(0);
  const [transcript, setTranscript] = useState<Line[]>([]);
  const [reply, setReply] = useState("");
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);
  const startedRef = useRef(false);

  // Start the session exactly once, after first paint. State lands only after the await.
  useEffect(() => {
    if (startedRef.current) return;
    startedRef.current = true;
    let active = true;
    void (async () => {
      const res = await tutor.start({ conceptId: concept, plannedMinutes: minutes });
      if (!active) return;
      if (!res.ok) {
        setError(res.message);
        return;
      }
      setSession(res.data);
      setStarting(res.data.budgetRemaining);
    })();
    return () => {
      active = false;
    };
  }, [concept, minutes]);

  async function takeHint() {
    if (!session || session.nextRung === null) return;
    const rungName = session.nextRung;
    const said = reply.trim();
    setBusy(true);
    setError(null);
    const res = await tutor.hint(session.sessionId, {
      rung: toRungParam(rungName),
      learnerSaid: said.length > 0 ? said : undefined,
    });
    setBusy(false);
    if (!res.ok) {
      setError(res.message);
      return;
    }
    setTranscript((t) => [
      ...t,
      ...(said.length > 0 ? [{ who: "you" as const, text: said }] : []),
      { who: "tutor" as const, text: res.data.tutor, rung: res.data.rung, cost: res.data.cost },
    ]);
    setSession(res.data.session);
    setReply("");
  }

  async function submitNote() {
    if (!session || note.trim().length < 20) return;
    setBusy(true);
    setError(null);
    const res = await tutor.stuckNote(session.sessionId, note.trim());
    setBusy(false);
    if (!res.ok) {
      setError(res.message);
      return;
    }
    setSession(res.data);
    setDone(true);
  }

  const exhausted = session !== null && isExhausted(session);

  return (
    <div className="min-h-dvh px-4 py-8 md:py-12">
      <div className="mx-auto max-w-2xl">
        <div className="flex items-center justify-between">
          <div className="serif text-[19px] font-semibold tracking-[0.14em] text-ink">PRAXIS</div>
          <Link href="/" className="text-[12.5px] text-ink-3 hover:text-ink-2">
            ← Today
          </Link>
        </div>

        <p className="mt-8 text-[11px] uppercase tracking-[0.2em] text-ink-3">
          The Session · the tutor cannot give you the answer
        </p>
        <h1 className="serif mt-3 text-[clamp(22px,3vw,30px)] font-normal leading-tight text-ink">
          It will get you to it. It will not hand it over.
        </h1>

        {error ? (
          <div
            role="alert"
            className="mt-6 rounded-md border border-serious/45 bg-surface px-4 py-3.5 text-[13px] text-ink-2"
          >
            {error}
          </div>
        ) : null}

        {session === null && !error ? (
          <p className="mt-8 text-[13.5px] text-ink-3">Opening the session…</p>
        ) : null}

        {session ? (
          <>
            <div className="mt-6">
              <BudgetMeter remaining={session.budgetRemaining} starting={starting} hints={session.hintsTaken} />
            </div>

            {transcript.length > 0 ? (
              <div className="mt-6 flex flex-col gap-3">
                {transcript.map((line, i) => (
                  <TranscriptLine key={i} line={line} />
                ))}
              </div>
            ) : (
              <p className="mt-6 rounded-md border border-line bg-surface px-4 py-3 text-[13.5px] text-ink-3">
                Start by orienting yourself — the first rung is free. Say what you already know about
                this, then take a hint.
              </p>
            )}

            {done ? (
              <DonePanel />
            ) : exhausted ? (
              <StuckNotePanel
                note={note}
                busy={busy}
                onNote={setNote}
                onSubmit={() => void submitNote()}
              />
            ) : (
              <NextRungPanel
                session={session}
                reply={reply}
                busy={busy}
                onReply={setReply}
                onTake={() => void takeHint()}
              />
            )}
          </>
        ) : null}
      </div>
    </div>
  );
}

function BudgetMeter({
  remaining,
  starting,
  hints,
}: {
  remaining: number;
  starting: number;
  hints: number;
}) {
  const frac = budgetFraction(remaining, starting);
  const pct = Math.round(frac * 100);
  const tone = frac > 0.5 ? "bg-accent" : frac > 0.2 ? "bg-warn" : "bg-critical";
  return (
    <div className="rounded-lg border border-line bg-surface p-4">
      <div className="flex items-baseline justify-between">
        <span className="text-[11px] uppercase tracking-[0.14em] text-ink-3">Answer budget</span>
        <span className="mono text-[15px] text-ink">
          {remaining}
          <span className="text-ink-4"> / {starting}</span>
        </span>
      </div>
      <div
        className="mt-2 h-2.5 overflow-hidden rounded-full bg-surface-2"
        role="progressbar"
        aria-valuenow={remaining}
        aria-valuemin={0}
        aria-valuemax={starting}
        aria-label="Answer budget remaining"
      >
        <div
          className={`h-full rounded-full transition-[width] duration-[300ms] ease-brand ${tone}`}
          style={{ width: `${pct}%` }}
        />
      </div>
      <p className="mt-2 text-[11px] text-ink-4">
        {hints} hint{hints === 1 ? "" : "s"} taken · it is your budget, and you are meant to watch it.
        It cannot be bought — only the answer would be cheaper, and that is never for sale.
      </p>
    </div>
  );
}

function TranscriptLine({ line }: { line: Line }) {
  if (line.who === "you") {
    return (
      <div className="ml-8 rounded-md border border-line bg-bg px-4 py-2.5">
        <div className="text-[10.5px] uppercase tracking-[0.12em] text-ink-4">You</div>
        <p className="mt-1 text-[13.5px] leading-relaxed text-ink-2">{line.text}</p>
      </div>
    );
  }
  return (
    <div className="mr-8 rounded-md border border-accent-line bg-surface px-4 py-2.5">
      <div className="flex items-center gap-2 text-[10.5px] uppercase tracking-[0.12em] text-accent">
        Tutor
        {line.rung ? (
          <span className="text-ink-4">
            · {line.rung}
            {typeof line.cost === "number" ? <span className="mono"> · {line.cost}</span> : null}
          </span>
        ) : null}
      </div>
      <p className="mt-1 text-[14px] leading-relaxed text-ink">{line.text}</p>
    </div>
  );
}

function NextRungPanel({
  session,
  reply,
  busy,
  onReply,
  onTake,
}: {
  session: SessionResponse;
  reply: string;
  busy: boolean;
  onReply: (v: string) => void;
  onTake: () => void;
}) {
  const rung = session.nextRung ?? "";
  const free = session.nextRungCost === 0;
  // LR-3: deeper rungs do not advance until you have said something.
  const needsReply = !free && reply.trim().length === 0;

  return (
    <div className="mt-6 rounded-lg border border-line bg-surface p-4">
      <label htmlFor="reply" className="text-[11px] uppercase tracking-[0.14em] text-ink-4">
        Your thinking so far
      </label>
      <textarea
        id="reply"
        value={reply}
        onChange={(e) => onReply(e.target.value)}
        rows={3}
        placeholder="Say where you have got to. The tutor moves from what you actually said, not from nothing."
        className="mt-2 w-full resize-y rounded-md border border-line bg-bg px-3 py-2.5 text-[14px] leading-relaxed text-ink placeholder:text-ink-4 focus:outline-none"
      />

      <div className="mt-3 flex flex-wrap items-center gap-3">
        <button
          type="button"
          disabled={busy || needsReply}
          onClick={onTake}
          className="inline-flex items-center justify-center rounded-md bg-accent px-4 py-2.5 text-[13.5px] font-medium text-accent-ink transition-[opacity] duration-[220ms] ease-brand hover:opacity-95 disabled:cursor-not-allowed disabled:bg-surface-2 disabled:text-ink-4"
        >
          {busy
            ? "Thinking…"
            : free
              ? `Take the ${rung} hint — free`
              : `Take the ${rung} hint — ${session.nextRungCost}`}
        </button>
        <span className="text-[12px] text-ink-4">{rungBlurb(session.nextRung)}</span>
      </div>
      {needsReply ? (
        <p className="mt-2 text-[11.5px] text-ink-4">
          Say something first — the ladder does not advance until you do.
        </p>
      ) : null}
    </div>
  );
}

function StuckNotePanel({
  note,
  busy,
  onNote,
  onSubmit,
}: {
  note: string;
  busy: boolean;
  onNote: (v: string) => void;
  onSubmit: () => void;
}) {
  const tooShort = note.trim().length < 20;
  return (
    <div className="mt-6 rounded-lg border border-warn/45 bg-surface p-4">
      <p className="text-[11px] uppercase tracking-[0.14em] text-warn">Budget spent</p>
      <p className="mt-2 text-[13.5px] leading-relaxed text-ink-2">
        PRAXIS has helped as much as it is going to on this concept. It is not going to tell you the
        answer — running out of budget was never a route to one. Instead, the most valuable two
        sentences in the session:
      </p>
      <label htmlFor="note" className="mt-4 block text-[13px] font-medium text-ink">
        Where exactly does your reasoning break?
      </label>
      <p className="text-[11.5px] text-ink-4">
        Not what you don&rsquo;t know — where the thing you <i>do</i> know stops being enough.
      </p>
      <textarea
        id="note"
        value={note}
        onChange={(e) => onNote(e.target.value)}
        rows={3}
        className="mt-2 w-full resize-y rounded-md border border-line bg-bg px-3 py-2.5 text-[14px] leading-relaxed text-ink placeholder:text-ink-4 focus:outline-none"
        placeholder="The last thing I was sure of was… and the first thing I was not was…"
      />
      <button
        type="button"
        disabled={busy || tooShort}
        onClick={onSubmit}
        className="mt-3 inline-flex items-center justify-center rounded-md bg-accent px-4 py-2.5 text-[13.5px] font-medium text-accent-ink hover:opacity-95 disabled:cursor-not-allowed disabled:bg-surface-2 disabled:text-ink-4"
      >
        {busy ? "Recording…" : "Record the note & reschedule"}
      </button>
    </div>
  );
}

function DonePanel() {
  return (
    <div className="mt-6 rounded-lg border border-good/45 bg-surface p-4">
      <p className="text-[11px] uppercase tracking-[0.14em] text-good">Not wasted</p>
      <p className="mt-2 text-[13.5px] leading-relaxed text-ink-2">
        That note is worth more to your plan than the answer would have been. PRAXIS has rescheduled
        this concept around exactly where you got stuck.
      </p>
      <div className="mt-3 flex flex-wrap gap-2">
        <Link
          href="/teach-back"
          className="inline-flex items-center justify-center rounded-md bg-accent px-4 py-2 text-[13px] font-medium text-accent-ink hover:opacity-95"
        >
          Teach it back — turn it into evidence →
        </Link>
        <Link
          href="/"
          className="inline-flex items-center justify-center rounded-md border border-line px-4 py-2 text-[13px] text-ink-2 hover:bg-surface-2"
        >
          ← Back to Today
        </Link>
      </div>
    </div>
  );
}
