"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";
import { diagnostics } from "@/lib/api";
import {
  answerIsReady,
  calibrate,
  calibrationVerdict,
  CONFIDENT_AT,
  INITIAL_DIAGNOSTIC,
  misconceptions,
  moveOf,
  receiveItem,
  recordAnswer,
  type AnsweredItem,
  type DiagnosticState,
} from "@/lib/diagnostic";

/*
  The Diagnostic (prototype §2). PRAXIS never assumes what you know: it hunts the boundary of your
  knowledge, escalating when you are right and de-escalating when you are not, and it is not trying
  to give you a good score. The whole thing exists to end on one screen — *what you think you know
  but don't* — which is only truthful because your confidence was captured before every verdict.

  The session and concept come from the URL (the Objective picks them in the real flow). If PRAXIS
  cannot be reached, the screen says so plainly and marks nothing — a broken dependency is never a
  verdict, least of all a verdict about a person.
*/

const NIL_GUID = "00000000-0000-0000-0000-000000000000";

/** Read a query param once, at first render. No effect, so nothing updates state synchronously on mount. */
function initialParam(name: string): string {
  if (typeof window === "undefined") return NIL_GUID;
  return new URLSearchParams(window.location.search).get(name) ?? NIL_GUID;
}

export default function DiagnosticPage() {
  const [session] = useState(() => initialParam("session"));
  const [concept] = useState(() => initialParam("concept"));

  const [state, setState] = useState<DiagnosticState>(INITIAL_DIAGNOSTIC);
  const [claim, setClaim] = useState("");
  const [selectedOption, setSelectedOption] = useState<number | null>(null);
  const [confidence, setConfidence] = useState(0.7);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const askedAt = useRef<number>(0);

  // Best-effort supply, never a gate: a failed top-up is not something the learner ever sees.
  // `nextItem`'s own refusal — an honest, rendered `stopped` state — is the only real gate.
  const topUpPool = useCallback(
    () => diagnostics.ensurePool(concept).catch(() => undefined),
    [concept],
  );

  const loadNext = useCallback(
    async (from: DiagnosticState) => {
      setBusy(true);
      setError(null);
      await topUpPool();
      const res = await diagnostics.nextItem(session, concept);
      setBusy(false);
      if (!res.ok) {
        setError(res.message);
        return;
      }
      setState(receiveItem(from, res.data));
      setClaim("");
      setSelectedOption(null);
      setConfidence(0.7);
      askedAt.current = Date.now();
    },
    [session, concept, topUpPool],
  );

  // The first question. State is updated only after the await resolves — never synchronously inside
  // the effect — so a mount fetch does not cascade renders.
  useEffect(() => {
    let active = true;
    void (async () => {
      await topUpPool();
      const res = await diagnostics.nextItem(session, concept);
      if (!active) return;
      if (!res.ok) {
        setError(res.message);
        return;
      }
      setState((s) => receiveItem(s, res.data));
      askedAt.current = Date.now();
    })();
    return () => {
      active = false;
    };
  }, [session, concept, topUpPool]);

  async function submit() {
    if (state.current === null || !answerIsReady({ claim, selectedOption }, state.current.format)) return;
    setBusy(true);
    setError(null);
    const seconds = Math.max(0, Math.round((Date.now() - askedAt.current) / 1000));
    const res = await diagnostics.submitAnswer(session, {
      itemId: state.current.itemId,
      ...(state.current.format === "MultipleChoice"
        ? { selectedOption: selectedOption! }
        : { claim: claim.trim() }),
      statedConfidence: confidence,
      seconds,
    });
    if (!res.ok) {
      // The oracle could not be reached. PRAXIS does not guess, and it never defaults to wrong.
      setBusy(false);
      setError(res.message);
      return;
    }
    const answered = recordAnswer(state, confidence, res.data);
    setState(answered);
    await loadNext(answered);
  }

  return (
    <div className="min-h-dvh px-4 py-10 md:py-14">
      <div className="mx-auto max-w-4xl">
        <div className="serif text-[19px] font-semibold tracking-[0.14em] text-ink">PRAXIS</div>

        <p className="mt-9 text-[11px] uppercase tracking-[0.2em] text-ink-3">
          The Diagnostic · seeding your Twin
        </p>
        <h1 className="serif mt-3 text-[clamp(24px,3.6vw,34px)] font-normal leading-[1.1] text-ink">
          The AI never assumes what you know.
        </h1>
        <p className="mt-4 max-w-prose text-[14.5px] leading-relaxed text-ink-2">
          It escalates when you are right and de-escalates when you are not. It is not trying to give
          you a good score — it is trying to find the edge of what you actually hold, and to catch the
          thing you are sure of and wrong about.
        </p>

        <hr className="my-8 border-line" />

        {error ? <ErrorPanel message={error} onRetry={() => void loadNext(state)} /> : null}

        {state.phase === "loading" && !error ? (
          <p className="text-[13.5px] text-ink-3">Choosing the question that tells PRAXIS the most…</p>
        ) : null}

        {state.phase === "asking" && state.current ? (
          <div className="grid gap-6 md:grid-cols-[1fr_260px]">
            <QuestionCard
              stem={state.current.stem}
              evidenceType={state.current.evidenceType}
              passage={state.current.passage}
              options={state.current.options}
              format={state.current.format}
              answered={state.history.length}
              claim={claim}
              selectedOption={selectedOption}
              confidence={confidence}
              busy={busy}
              onClaim={setClaim}
              onSelect={setSelectedOption}
              onConfidence={setConfidence}
              onSubmit={() => void submit()}
            />
            <TwinSidebar state={state} />
          </div>
        ) : null}

        {state.phase === "stopped" ? <CalibrationScreen state={state} /> : null}
      </div>
    </div>
  );
}

function QuestionCard({
  stem,
  evidenceType,
  passage,
  options,
  format,
  answered,
  claim,
  selectedOption,
  confidence,
  busy,
  onClaim,
  onSelect,
  onConfidence,
  onSubmit,
}: {
  stem: string;
  evidenceType: string | null;
  passage: string | null;
  options: string[] | null;
  format: string | null;
  answered: number;
  claim: string;
  selectedOption: number | null;
  confidence: number;
  busy: boolean;
  onClaim: (v: string) => void;
  onSelect: (v: number) => void;
  onConfidence: (v: number) => void;
  onSubmit: () => void;
}) {
  return (
    <div className="rounded-lg border border-line bg-surface p-5">
      <div className="flex flex-wrap items-center gap-2">
        <span className="rounded-full border border-accent-line bg-[color:var(--accent-soft)] px-2.5 py-0.5 text-[11px] text-accent">
          Question {answered + 1}
        </span>
        {evidenceType ? (
          <span className="rounded-full border border-line px-2.5 py-0.5 text-[11px] text-ink-3">
            {evidenceType}
          </span>
        ) : null}
      </div>

      {passage ? (
        <>
          <p className="mt-4 text-[11px] uppercase tracking-[0.14em] text-ink-4">The passage</p>
          <div className="mt-2 max-h-[52vh] overflow-y-auto pr-2.5">
            <div className="serif max-w-[66ch] text-[16.5px] leading-[1.75] text-ink">
              {passage.split(/\n\s*\n/).map((para, i) => (
                <p key={i} className="mb-[0.95em] last:mb-0">
                  {para}
                </p>
              ))}
            </div>
          </div>
          <p className="mt-3.5 border-t border-line-soft pt-3 text-[11.5px] leading-relaxed text-ink-4">
            Re-read it as often as you like. Your time is recorded because it is data about you — it
            is not a score, and reading slowly has never been marked down.
          </p>
        </>
      ) : null}

      <p className="serif mt-4 text-[19px] leading-snug text-ink">{stem}</p>

      {options ? (
        <fieldset className="mt-4 min-w-0 border-0 p-0">
          <legend className="text-[11px] uppercase tracking-[0.14em] text-ink-4">Choose one</legend>
          {options.map((option, index) => (
            <label key={index} className="relative mt-2 block">
              <input
                type="radio"
                name="option"
                checked={selectedOption === index}
                onChange={() => onSelect(index)}
                className="peer absolute inset-0 h-full w-full cursor-pointer opacity-0"
              />
              <span
                className={`flex min-h-12 items-start gap-3 rounded-md border px-3.5 py-2.5 peer-focus-visible:outline peer-focus-visible:outline-2 peer-focus-visible:outline-accent peer-focus-visible:outline-offset-2 ${
                  selectedOption === index
                    ? "border-accent-line bg-[color:var(--accent-soft)]"
                    : "border-line bg-bg"
                }`}
              >
                <span
                  aria-hidden="true"
                  className={`mono mt-px grid h-[22px] w-[22px] shrink-0 place-items-center rounded-full border text-[11px] ${
                    selectedOption === index
                      ? "border-accent bg-accent font-bold text-accent-ink"
                      : "border-line bg-surface text-ink-3"
                  }`}
                >
                  {String.fromCharCode(65 + index)}
                </span>
                <span className={`text-[14px] leading-normal ${selectedOption === index ? "text-ink" : "text-ink-2"}`}>
                  {option}
                </span>
              </span>
            </label>
          ))}
        </fieldset>
      ) : (
        <>
          <label
            htmlFor="claim"
            className="mt-5 block text-[11px] uppercase tracking-[0.14em] text-ink-4"
          >
            Your answer — in your own words
          </label>
          <textarea
            id="claim"
            value={claim}
            onChange={(e) => onClaim(e.target.value)}
            rows={4}
            className="mt-2 w-full resize-y rounded-md border border-line bg-bg px-3 py-2.5 text-[14px] leading-relaxed text-ink placeholder:text-ink-4 focus:outline-none"
            placeholder="Explain it as if the answer were not a multiple choice — PRAXIS grades the reasoning."
          />
        </>
      )}

      <ConfidenceSlider value={confidence} onChange={onConfidence} />

      <button
        type="button"
        disabled={busy || !answerIsReady({ claim, selectedOption }, format)}
        onClick={onSubmit}
        className="mt-4 inline-flex items-center justify-center rounded-md bg-accent px-5 py-2.5 text-[13.5px] font-medium text-accent-ink transition-[opacity] duration-[220ms] ease-brand hover:opacity-95 disabled:cursor-not-allowed disabled:bg-surface-2 disabled:text-ink-4"
      >
        {busy ? "Checking…" : "Submit answer"}
      </button>
    </div>
  );
}

function ConfidenceSlider({ value, onChange }: { value: number; onChange: (v: number) => void }) {
  const pct = Math.round(value * 100);
  const sure = value >= CONFIDENT_AT;
  return (
    <div className="mt-5 rounded-md border border-line bg-bg px-3.5 py-3">
      <div className="flex items-baseline justify-between">
        <label htmlFor="confidence" className="text-[11px] uppercase tracking-[0.14em] text-ink-4">
          How sure are you?
        </label>
        <span className={`mono text-[15px] ${sure ? "text-accent" : "text-ink-2"}`}>{pct}%</span>
      </div>
      <input
        id="confidence"
        type="range"
        min={0.5}
        max={0.95}
        step={0.05}
        value={value}
        onChange={(e) => onChange(Number(e.target.value))}
        className="mt-2 w-full accent-[var(--color-accent)]"
      />
      <p className="mt-2 text-[11.5px] leading-relaxed text-ink-4">
        Stated <b className="text-ink-3">before</b> you find out. Being wrong and knowing you might be
        is healthy; being wrong and <b className="text-ink-3">sure</b> is the only thing on your report
        you could not have worked out for yourself.
      </p>
    </div>
  );
}

function TwinSidebar({ state }: { state: DiagnosticState }) {
  const theta = state.current?.theta ?? 0;
  const se = state.current?.standardError ?? 1;
  const misses = misconceptions(state);
  return (
    <aside className="h-fit rounded-lg border border-line bg-surface p-4 md:sticky md:top-8">
      <div className="flex items-baseline justify-between">
        <h2 className="text-[11px] uppercase tracking-[0.14em] text-ink-3">Twin · seeding live</h2>
        <span className="mono text-[11px] text-ink-4">{state.history.length} answered</span>
      </div>

      <div className="mt-3 rounded-md border border-line bg-bg px-3 py-2.5">
        <div className="text-[10.5px] uppercase tracking-[0.12em] text-ink-4">
          Ability estimate ± uncertainty
        </div>
        <div className="mono mt-1 text-[15px] text-ink">
          θ {theta.toFixed(2)} <span className="text-ink-4">± {se.toFixed(2)}</span>
        </div>
        <p className="mt-1 text-[10.5px] text-ink-4">
          θ alone is a number; a number alone always looks like knowledge. So the doubt travels with
          it.
        </p>
      </div>

      {state.history.length > 0 ? (
        <div className="mt-4">
          <div className="text-[10.5px] uppercase tracking-[0.12em] text-ink-4">Estimate trail</div>
          <ul className="mt-2 flex flex-wrap gap-1.5">
            {state.history.map((h, i) => (
              <li key={h.itemId + i}>
                <MovePip item={h} />
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {misses.length > 0 ? (
        <div className="mt-4">
          <div className="text-[10.5px] uppercase tracking-[0.12em] text-critical">
            Misconceptions detected
          </div>
          <ul className="mt-2 flex flex-col gap-1.5">
            {misses.map((m) => (
              <li
                key={m}
                className="mono rounded border border-critical/40 bg-bg px-2 py-1 text-[11px] text-ink-2"
              >
                {m}
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </aside>
  );
}

function MovePip({ item }: { item: AnsweredItem }) {
  const move = moveOf(item);
  const glyph = move === "rose" ? "↑" : move === "fell" ? "↓" : "→";
  const tone =
    item.andYouWere === "right" ? "border-good/45 text-good" : "border-serious/45 text-serious";
  return (
    <span
      title={`${item.andYouWere === "right" ? "right" : "not right"} · estimate ${move}`}
      className={`mono grid h-6 w-6 place-items-center rounded border text-[12px] ${tone}`}
    >
      {glyph}
    </span>
  );
}

function CalibrationScreen({ state }: { state: DiagnosticState }) {
  const c = calibrate(state);
  const overRed = c.overconfidencePoints > 0;
  return (
    <div className="mx-auto max-w-2xl">
      {state.stopSays ? (
        <p className="rounded-md border border-line bg-surface px-4 py-3 text-[13px] text-ink-2">
          {state.stopSays}
        </p>
      ) : null}

      <p className="mt-8 text-[11px] uppercase tracking-[0.2em] text-ink-3">The Diagnostic · result</p>
      <h2 className="serif mt-3 text-[clamp(24px,3.6vw,34px)] font-normal leading-[1.1] text-ink">
        What you think you know but don&rsquo;t.
      </h2>
      <p className="mt-4 text-[14.5px] leading-relaxed text-ink-2">{calibrationVerdict(c)}</p>

      <div className="mt-6 grid grid-cols-3 gap-3">
        <Tile label="Answered" value={String(c.answered)} />
        <Tile label="Right" value={`${c.correct}/${c.answered}`} />
        <Tile
          label="Confidence − competence"
          value={`${c.overconfidencePoints > 0 ? "+" : ""}${c.overconfidencePoints} pts`}
          tone={overRed ? "text-serious" : c.overconfidencePoints < 0 ? "text-s1" : "text-good"}
        />
      </div>

      {c.blindspots.length > 0 ? (
        <section className="mt-7">
          <h3 className="text-[11px] uppercase tracking-[0.14em] text-critical">
            Sure, and wrong — your blind spots
          </h3>
          <ul className="mt-2 flex flex-col gap-2">
            {c.blindspots.map((b, i) => (
              <li
                key={b.itemId + i}
                className="rounded-md border border-critical/40 bg-surface px-4 py-3"
              >
                <p className="text-[13.5px] text-ink">{b.stem}</p>
                <p className="mono mt-1 text-[11px] text-ink-4">
                  you said {Math.round(b.statedConfidence * 100)}% · you were not right
                </p>
              </li>
            ))}
          </ul>
        </section>
      ) : null}

      {c.underclaimed.length > 0 ? (
        <section className="mt-6">
          <h3 className="text-[11px] uppercase tracking-[0.14em] text-s1">
            Right, but you doubted it
          </h3>
          <p className="mt-2 text-[13px] text-ink-3">
            {c.underclaimed.length} answer{c.underclaimed.length === 1 ? "" : "s"} you got right while
            unsure — knowledge you are not yet spending.
          </p>
        </section>
      ) : null}

      <div className="mt-8 border-t border-line pt-6">
        <Link
          href="/contract"
          className="inline-flex items-center justify-center rounded-md bg-accent px-5 py-2.5 text-[13.5px] font-medium text-accent-ink hover:opacity-95"
        >
          Now the Contract — what your minutes actually buy →
        </Link>
      </div>
    </div>
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
      className="mb-6 flex flex-col gap-3 rounded-md border border-serious/45 bg-surface px-4 py-3.5 sm:flex-row sm:items-center sm:justify-between"
    >
      <p className="text-[13px] text-ink-2">{message}</p>
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
