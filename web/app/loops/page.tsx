"use client";

import { useEffect, useState } from "react";
import { AppShell } from "@/components/AppShell";
import {
  reality,
  type CommitPredictionResponse,
  type LoopsBoardResponse,
  type OpenLoopResponse,
  type ResolveResponse,
} from "@/lib/api";
import {
  confidenceLabel,
  coverageLabel,
  horizonLabel,
  KINDS,
  type Kind,
  verdictLine,
} from "@/lib/loops";

/*
  Loops (prototype §8). You say what code will do and how sure you are; it goes on the board and it
  cannot be taken back. There is no withdraw button and no hide filter — an open loop stays until
  reality closes it, exactly once. Resolution runs the frozen code and reports the Brier honestly:
  sure-and-wrong is the most expensive score there is, and it is only sayable because the confidence
  was frozen first.
*/

export default function LoopsPage() {
  const [board, setBoard] = useState<LoopsBoardResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let active = true;
    void (async () => {
      const res = await reality.board();
      if (!active) return;
      setLoading(false);
      if (!res.ok) {
        setError(res.message);
        return;
      }
      setError(null);
      setBoard(res.data);
    })();
    return () => {
      active = false;
    };
  }, [reloadKey]);

  const reload = () => setReloadKey((k) => k + 1);

  return (
    <AppShell crumb="Loops" current="Loops">
      <div className="mx-auto max-w-2xl">
        <p className="text-[11px] uppercase tracking-[0.18em] text-ink-3">Predictions against reality</p>
        <h1 className="serif mt-2 text-[clamp(22px,2.6vw,30px)] font-normal leading-tight text-ink">
          Say what will happen. Then it stands.
        </h1>
        <p className="mt-3 max-w-prose text-[14px] leading-relaxed text-ink-2">
          You commit to what a program will do and how sure you are — before it runs. There is no
          withdraw and no hide: a prediction stays on the board until reality settles it, and it is
          judged exactly once. This is the calibration gym.
        </p>

        <div className="mt-6">
          {loading ? (
            <p className="text-[13.5px] text-ink-3">Reading the board…</p>
          ) : error ? (
            <ErrorPanel message={error} onRetry={reload} />
          ) : board ? (
            <>
              <Board board={board} onResolved={reload} />
              <NewPrediction onCommitted={reload} />
            </>
          ) : null}
        </div>
      </div>
    </AppShell>
  );
}

function Board({ board, onResolved }: { board: LoopsBoardResponse; onResolved: () => void }) {
  return (
    <section>
      <p className="rounded-md border border-line bg-surface px-4 py-3 text-[13.5px] leading-relaxed text-ink-2">
        {board.headline}
      </p>

      <div className="mt-4 grid grid-cols-3 gap-3">
        <Tile label="Open" value={String(board.open.length)} />
        <Tile label="Resolved" value={String(board.resolved)} />
        <Tile label="Coverage" value={coverageLabel(board.coverage)} tone="text-accent" />
      </div>

      {board.open.length > 0 ? (
        <ul className="mt-5 flex flex-col gap-2">
          {board.open.map((loop) => (
            <LoopRow key={loop.loopId} loop={loop} onResolved={onResolved} />
          ))}
        </ul>
      ) : (
        <p className="mt-5 text-center text-[13px] text-ink-3">
          No open predictions. Make one below — and mean it.
        </p>
      )}
    </section>
  );
}

function LoopRow({ loop, onResolved }: { loop: OpenLoopResponse; onResolved: () => void }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<ResolveResponse | null>(null);

  async function resolve() {
    setBusy(true);
    setError(null);
    const res = await reality.resolve(loop.loopId);
    setBusy(false);
    if (!res.ok) {
      // A sandbox outage is not a verdict — the loop stays open and nothing is marked.
      setError(res.message);
      return;
    }
    setResult(res.data);
    onResolved();
  }

  return (
    <li
      className={`rounded-lg border bg-surface p-4 ${loop.isOverdue ? "border-warn/45" : "border-line"}`}
    >
      <div className="flex items-baseline justify-between gap-3">
        <span className="text-[14px] text-ink">{loop.claim}</span>
        <span className="mono shrink-0 text-[11.5px] text-ink-4">
          {confidenceLabel(loop.confidence)} · {horizonLabel(loop)}
        </span>
      </div>

      {result ? (
        <ResolvedVerdict result={result} />
      ) : (
        <div className="mt-3 flex flex-wrap items-center gap-3">
          <button
            type="button"
            disabled={busy}
            onClick={() => void resolve()}
            className="rounded-md border border-line px-3.5 py-1.5 text-[12.5px] text-ink-2 hover:bg-surface-2 disabled:cursor-not-allowed disabled:text-ink-4"
          >
            {busy ? "Running the code…" : "Resolve against reality"}
          </button>
          {error ? (
            <span className="text-[11.5px] text-serious" role="alert">
              {error} Nothing was marked — the loop is still open.
            </span>
          ) : null}
        </div>
      )}
    </li>
  );
}

function ResolvedVerdict({ result }: { result: ResolveResponse }) {
  return (
    <div
      className={`mt-3 rounded-md border px-3.5 py-2.5 ${
        result.correct ? "border-good/45 bg-bg" : "border-serious/45 bg-bg"
      }`}
    >
      <div className="flex items-baseline justify-between">
        <span className={`text-[11px] uppercase tracking-[0.14em] ${result.correct ? "text-good" : "text-serious"}`}>
          {result.correct ? "Right" : "Wrong"}
        </span>
        <span className="mono text-[11.5px] text-ink-4">Brier {result.brierScore.toFixed(2)}</span>
      </div>
      <p className="mt-1.5 text-[12.5px] leading-relaxed text-ink-2">{verdictLine(result)}</p>
      <p className="mono mt-1.5 text-[11px] text-ink-4">
        it actually did: {result.groundTruth || "—"}
      </p>
    </div>
  );
}

function NewPrediction({ onCommitted }: { onCommitted: () => void }) {
  const [kind, setKind] = useState<Kind>("Output");
  const [code, setCode] = useState("");
  const [answer, setAnswer] = useState("");
  const [confidence, setConfidence] = useState(0.7);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [receipt, setReceipt] = useState<CommitPredictionResponse | null>(null);

  const active = KINDS.find((k) => k.value === kind)!;

  async function commit() {
    if (code.trim() === "" || answer.trim() === "") return;
    setBusy(true);
    setError(null);
    const res = await reality.commit({ kind, code: code.trim(), answer: answer.trim(), confidence });
    setBusy(false);
    if (!res.ok) {
      setError(res.message);
      return;
    }
    setReceipt(res.data);
    setCode("");
    setAnswer("");
    setConfidence(0.7);
    onCommitted();
  }

  return (
    <section className="mt-8 rounded-lg border border-line bg-surface p-5">
      <h2 className="serif text-[18px] font-normal text-ink">Make a prediction</h2>
      <p className="mt-1 text-[12px] text-ink-4">
        Once you commit, it is on the board and cannot be taken back.
      </p>

      <div className="mt-4 flex flex-wrap gap-2" role="group" aria-label="Kind of prediction">
        {KINDS.map((k) => (
          <button
            key={k.value}
            type="button"
            aria-pressed={kind === k.value}
            onClick={() => setKind(k.value)}
            className={`rounded-md border px-3 py-1.5 text-[12.5px] transition-[background-color,border-color] duration-[220ms] ease-brand ${
              kind === k.value
                ? "border-accent-line bg-surface-2 text-ink"
                : "border-line text-ink-3 hover:border-ink-4"
            }`}
          >
            {k.label}
          </button>
        ))}
      </div>

      <label htmlFor="code" className="mt-4 block text-[11px] uppercase tracking-[0.14em] text-ink-4">
        The program
      </label>
      <textarea
        id="code"
        value={code}
        onChange={(e) => setCode(e.target.value)}
        rows={4}
        placeholder="print(2 + 2)"
        className="mono mt-2 w-full resize-y rounded-md border border-line bg-bg px-3 py-2.5 text-[13px] leading-relaxed text-ink placeholder:text-ink-4 focus:outline-none"
      />

      <label htmlFor="answer" className="mt-4 block text-[11px] uppercase tracking-[0.14em] text-ink-4">
        Your prediction
      </label>
      <input
        id="answer"
        value={answer}
        onChange={(e) => setAnswer(e.target.value)}
        placeholder={active.hint}
        className="mt-2 w-full rounded-md border border-line bg-bg px-3 py-2.5 text-[14px] text-ink placeholder:text-ink-4 focus:outline-none"
      />

      <div className="mt-4 rounded-md border border-line bg-bg px-3.5 py-3">
        <div className="flex items-baseline justify-between">
          <label htmlFor="confidence" className="text-[11px] uppercase tracking-[0.14em] text-ink-4">
            How sure are you?
          </label>
          <span className="mono text-[14px] text-accent">{confidenceLabel(confidence)}</span>
        </div>
        <input
          id="confidence"
          type="range"
          min={0.5}
          max={0.95}
          step={0.05}
          value={confidence}
          onChange={(e) => setConfidence(Number(e.target.value))}
          className="mt-2 w-full accent-[var(--color-accent)]"
        />
        <p className="mt-1.5 text-[11px] text-ink-4">
          Stated before you find out — it is what makes this a calibration exercise and not a quiz.
        </p>
      </div>

      <button
        type="button"
        disabled={busy || code.trim() === "" || answer.trim() === ""}
        onClick={() => void commit()}
        className="mt-4 inline-flex items-center justify-center rounded-md bg-accent px-5 py-2.5 text-[13.5px] font-medium text-accent-ink transition-[opacity] duration-[220ms] ease-brand hover:opacity-95 disabled:cursor-not-allowed disabled:bg-surface-2 disabled:text-ink-4"
      >
        {busy ? "Committing…" : "Commit — no takebacks"}
      </button>

      {error ? (
        <p className="mt-3 text-[12.5px] text-serious" role="alert">
          {error}
        </p>
      ) : null}
      {receipt ? (
        <p className="mt-3 text-[12.5px] text-good">
          Committed. It is on the board now — resolve it whenever you are ready to find out.
        </p>
      ) : null}
    </section>
  );
}

function Tile({ label, value, tone = "text-ink" }: { label: string; value: string; tone?: string }) {
  return (
    <div className="rounded-md border border-line bg-surface px-3 py-3">
      <div className="text-[10.5px] uppercase tracking-[0.1em] text-ink-4">{label}</div>
      <div className={`mono mt-1 text-[20px] ${tone}`}>{value}</div>
    </div>
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
