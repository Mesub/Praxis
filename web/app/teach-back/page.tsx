"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { teachBack, type TeachBackResponse } from "@/lib/api";
import { outcomeOf, scoreLabel } from "@/lib/teachback";

/*
  Teach-back (prototype §4, the rubric box). Explain it in your own words to someone who has never
  heard of it — the Feynman test — and a rubric on the far side grades it. What comes back is the
  verdict, the score, and any misconception it surfaced. What does NOT come back is the rubric: the
  bands are, in practice, the answer, so you are shown how you did, never the marking scheme. And it
  says the quiet part out loud: a model graded this, so it can raise belief but never, alone, make
  you held (C-4).
*/

const NIL_GUID = "00000000-0000-0000-0000-000000000000";

function initialParam(name: string): string {
  if (typeof window === "undefined") return NIL_GUID;
  return new URLSearchParams(window.location.search).get(name) ?? NIL_GUID;
}

export default function TeachBackPage() {
  const [concept] = useState(() => initialParam("concept"));
  const [session] = useState(() => initialParam("session"));

  const [text, setText] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<TeachBackResponse | null>(null);
  const startedAt = useRef<number>(0);

  // Stamp the clock when the page opens (and again on retry, in the handler). In an effect, not in
  // render — Date.now() and refs are impure/off-limits during render.
  useEffect(() => {
    startedAt.current = Date.now();
  }, []);

  async function submit() {
    if (text.trim() === "") return;
    setBusy(true);
    setError(null);
    const seconds = Math.max(0, Math.round((Date.now() - startedAt.current) / 1000));
    const res = await teachBack.submit({
      sessionId: session,
      conceptId: concept,
      text: text.trim(),
      seconds,
    });
    setBusy(false);
    if (!res.ok) {
      // The grader could not be reached. Nothing was marked — a broken dependency is never a verdict.
      setError(res.message);
      return;
    }
    setResult(res.data);
  }

  function again() {
    setResult(null);
    setError(null);
    setText("");
    startedAt.current = Date.now();
  }

  return (
    <div className="min-h-dvh px-4 py-10 md:py-14">
      <div className="mx-auto max-w-2xl">
        <div className="flex items-center justify-between">
          <div className="serif text-[19px] font-semibold tracking-[0.14em] text-ink">PRAXIS</div>
          <Link href="/" className="text-[12.5px] text-ink-3 hover:text-ink-2">
            ← Today
          </Link>
        </div>

        <p className="mt-9 text-[11px] uppercase tracking-[0.2em] text-ink-3">
          Teach-back · EXPLAIN evidence
        </p>
        <h1 className="serif mt-3 text-[clamp(24px,3.6vw,34px)] font-normal leading-[1.1] text-ink">
          Explain it to someone who has never heard of it.
        </h1>
        <p className="mt-4 max-w-prose text-[14.5px] leading-relaxed text-ink-2">
          In your own words — not a definition you can recite. A rubric grades the explanation; the
          speech (the &ldquo;um&rdquo;s and false starts) is stripped before it is marked, so you are
          judged on the idea, not the delivery. It is evidence, not a checkbox.
        </p>

        <hr className="my-8 border-line" />

        {error ? (
          <ErrorPanel message={error} onRetry={() => void submit()} />
        ) : null}

        {result ? (
          <Result result={result} onAgain={again} />
        ) : (
          <div className="rounded-lg border border-line bg-surface p-5">
            <label htmlFor="explanation" className="text-[11px] uppercase tracking-[0.14em] text-ink-4">
              Your explanation
            </label>
            <textarea
              id="explanation"
              value={text}
              onChange={(e) => setText(e.target.value)}
              rows={7}
              placeholder="Start with what it is for, then how it works, then what it costs. Teach it — do not define it."
              className="mt-2 w-full resize-y rounded-md border border-line bg-bg px-3 py-2.5 text-[14px] leading-relaxed text-ink placeholder:text-ink-4 focus:outline-none"
            />
            <button
              type="button"
              disabled={busy || text.trim() === ""}
              onClick={() => void submit()}
              className="mt-4 inline-flex items-center justify-center rounded-md bg-accent px-5 py-2.5 text-[13.5px] font-medium text-accent-ink transition-[opacity] duration-[220ms] ease-brand hover:opacity-95 disabled:cursor-not-allowed disabled:bg-surface-2 disabled:text-ink-4"
            >
              {busy ? "Grading…" : "Explain it back"}
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

function Result({ result, onAgain }: { result: TeachBackResponse; onAgain: () => void }) {
  const p = outcomeOf(result.outcome);
  return (
    <div className="rounded-lg border border-line bg-surface p-5">
      <div className="flex items-baseline justify-between gap-3">
        <span className={`text-[11px] font-medium uppercase tracking-[0.14em] ${p.tone}`}>
          {p.label}
        </span>
        <span className="mono text-[22px] text-ink">{scoreLabel(result.score)}</span>
      </div>
      <p className="mt-2 text-[13.5px] leading-relaxed text-ink-2">{p.gloss}</p>

      {result.misconceptions.length > 0 ? (
        <section className="mt-5">
          <h2 className="text-[11px] uppercase tracking-[0.14em] text-critical">
            Beliefs the rubric surfaced
          </h2>
          <ul className="mt-2 flex flex-col gap-1.5">
            {result.misconceptions.map((m) => (
              <li
                key={m}
                className="mono rounded border border-critical/40 bg-bg px-2.5 py-1 text-[11.5px] text-ink-2"
              >
                {m}
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      <div className="mt-5 rounded-md border border-accent-line bg-[color:var(--accent-soft)] px-4 py-3.5">
        <div className="flex items-center gap-2 text-[11px] uppercase tracking-[0.14em] text-accent">
          {result.raisedBelief ? "Evidence accepted" : "No belief moved"}
        </div>
        <p className="mt-2 text-[13px] leading-relaxed text-ink-2">{result.ceiling}</p>
      </div>

      <p className="mt-4 text-[11.5px] text-ink-4">
        You are shown the verdict, not the rubric. The bands are close to the answer, so they stay on
        PRAXIS&rsquo;s side of the line — the same rule as the tutor.
      </p>

      <button
        type="button"
        onClick={onAgain}
        className="mt-4 rounded-md border border-line px-4 py-2 text-[13px] text-ink-2 hover:bg-surface-2"
      >
        Explain it again
      </button>
    </div>
  );
}

function ErrorPanel({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div
      role="alert"
      className="mb-6 flex flex-col gap-3 rounded-md border border-serious/45 bg-surface px-4 py-3.5 sm:flex-row sm:items-center sm:justify-between"
    >
      <p className="text-[13px] text-ink-2">{message} Nothing was marked against you.</p>
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
