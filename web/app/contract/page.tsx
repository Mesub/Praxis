"use client";

import { useMemo, useState } from "react";
import { compiler, type PlanResponse } from "@/lib/api";
import {
  alternativeOffer,
  effectiveMinutesPerDay,
  maintenanceSharePercent,
  planHeadline,
  reachedFraction,
  runningToStandStill,
  wallExplanation,
} from "@/lib/contract";

/*
  The Contract (prototype §1, contract card). You give PRAXIS the minutes you will actually spend and
  the day you want it by, and it compiles the shortest honest path — then tells you whether it
  believes in it. When it hits the Decay Wall (C-1) it does the thing no other product will: it says
  no, shows you the forward simulation it said no *from*, and offers the narrower objective it does
  believe in. Stake and witness (I-9) are not in Phase 0, so they are not pretended here.
*/

const NIL_GUID = "00000000-0000-0000-0000-000000000000";

function initialConcept(): string {
  if (typeof window === "undefined") return NIL_GUID;
  return new URLSearchParams(window.location.search).get("concept") ?? NIL_GUID;
}

type Phase = "form" | "compiling" | "result";

export default function ContractPage() {
  const [concept] = useState(initialConcept);
  const [minutes, setMinutes] = useState(45);
  const [daysPerWeek, setDaysPerWeek] = useState(6);
  const [weeks, setWeeks] = useState(12);

  const [phase, setPhase] = useState<Phase>("form");
  const [plan, setPlan] = useState<PlanResponse | null>(null);
  const [error, setError] = useState<string | null>(null);

  const effective = useMemo(() => effectiveMinutesPerDay(minutes, daysPerWeek), [minutes, daysPerWeek]);
  const deadline = useMemo(() => {
    const d = new Date();
    d.setDate(d.getDate() + weeks * 7);
    return d;
  }, [weeks]);

  async function compile(atMinutes: number, atDays: number) {
    const eff = effectiveMinutesPerDay(atMinutes, atDays);
    setPhase("compiling");
    setError(null);

    const set = await compiler.setObjective({
      conceptId: concept,
      minutesPerDay: eff,
      deadline: deadline.toISOString(),
    });
    if (!set.ok) {
      setPhase("form");
      setError(set.message);
      return;
    }

    const got = await compiler.plan();
    if (!got.ok) {
      setPhase("form");
      setError(got.message);
      return;
    }
    setPlan(got.data);
    setPhase("result");
  }

  function reviseTo(newMinutes: number) {
    setMinutes(newMinutes);
    setDaysPerWeek(7);
    void compile(newMinutes, 7);
  }

  return (
    <div className="min-h-dvh px-4 py-10 md:py-14">
      <div className="mx-auto max-w-2xl">
        <div className="serif text-[19px] font-semibold tracking-[0.14em] text-ink">PRAXIS</div>

        <p className="mt-9 text-[11px] uppercase tracking-[0.2em] text-ink-3">The Contract</p>
        <h1 className="serif mt-3 text-[clamp(24px,3.6vw,34px)] font-normal leading-[1.1] text-ink">
          Give it the minutes you will actually spend.
        </h1>
        <p className="mt-4 max-w-prose text-[14.5px] leading-relaxed text-ink-2">
          Not the number you wish were true. PRAXIS takes it literally and runs the forward
          simulation — and if the objective does not survive being maintained at that pace, it will
          tell you before you start, not eighteen months in.
        </p>

        <hr className="my-8 border-line" />

        {error ? (
          <div
            role="alert"
            className="mb-6 rounded-md border border-serious/45 bg-surface px-4 py-3.5 text-[13px] text-ink-2"
          >
            {error}
          </div>
        ) : null}

        {phase !== "result" ? (
          <ContractForm
            minutes={minutes}
            daysPerWeek={daysPerWeek}
            weeks={weeks}
            effective={effective}
            deadline={deadline}
            compiling={phase === "compiling"}
            onMinutes={setMinutes}
            onDays={setDaysPerWeek}
            onWeeks={setWeeks}
            onCompile={() => void compile(minutes, daysPerWeek)}
          />
        ) : null}

        {phase === "result" && plan ? (
          <Result plan={plan} onRevise={reviseTo} onBack={() => setPhase("form")} />
        ) : null}
      </div>
    </div>
  );
}

function ContractForm({
  minutes,
  daysPerWeek,
  weeks,
  effective,
  deadline,
  compiling,
  onMinutes,
  onDays,
  onWeeks,
  onCompile,
}: {
  minutes: number;
  daysPerWeek: number;
  weeks: number;
  effective: number;
  deadline: Date;
  compiling: boolean;
  onMinutes: (v: number) => void;
  onDays: (v: number) => void;
  onWeeks: (v: number) => void;
  onCompile: () => void;
}) {
  return (
    <div className="rounded-lg border border-line bg-surface p-5">
      <Slider
        id="minutes"
        label="Minutes per day"
        value={minutes}
        display={`${minutes} min`}
        min={10}
        max={120}
        step={5}
        onChange={onMinutes}
      />
      <Slider
        id="days"
        label="Days per week"
        value={daysPerWeek}
        display={`${daysPerWeek} / 7`}
        min={3}
        max={7}
        step={1}
        onChange={onDays}
      />
      <Slider
        id="weeks"
        label="Deadline"
        value={weeks}
        display={deadline.toLocaleDateString(undefined, { day: "numeric", month: "short", year: "numeric" })}
        min={4}
        max={52}
        step={1}
        onChange={onWeeks}
      />

      <div className="mt-4 rounded-md border border-line bg-bg px-3.5 py-3">
        <div className="flex items-baseline justify-between">
          <span className="text-[11px] uppercase tracking-[0.14em] text-ink-4">
            Compiles against
          </span>
          <span className="mono text-[15px] text-accent">≈ {effective} min/day</span>
        </div>
        <p className="mt-1 text-[11.5px] leading-relaxed text-ink-4">
          {weeks} weeks · {minutes} minutes on each of {daysPerWeek} days, averaged over the week —
          because the Decay Wall counts minutes of progress per calendar day, not how you feel about
          your schedule.
        </p>
      </div>

      <button
        type="button"
        disabled={compiling}
        onClick={onCompile}
        className="mt-5 inline-flex w-full items-center justify-center rounded-md bg-accent px-5 py-3 text-[14px] font-medium text-accent-ink transition-[opacity] duration-[220ms] ease-brand hover:opacity-95 disabled:cursor-not-allowed disabled:bg-surface-2 disabled:text-ink-4"
      >
        {compiling ? "Compiling the forward simulation…" : "Sign the contract & compile"}
      </button>
    </div>
  );
}

function Slider({
  id,
  label,
  value,
  display,
  min,
  max,
  step,
  onChange,
}: {
  id: string;
  label: string;
  value: number;
  display: string;
  min: number;
  max: number;
  step: number;
  onChange: (v: number) => void;
}) {
  return (
    <div className="mb-4">
      <div className="flex items-baseline justify-between">
        <label htmlFor={id} className="text-[12.5px] text-ink-2">
          {label}
        </label>
        <span className="mono text-[13px] text-ink">{display}</span>
      </div>
      <input
        id={id}
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={(e) => onChange(Number(e.target.value))}
        className="mt-2 w-full accent-[var(--color-accent)]"
      />
    </div>
  );
}

function Result({
  plan,
  onRevise,
  onBack,
}: {
  plan: PlanResponse;
  onRevise: (minutes: number) => void;
  onBack: () => void;
}) {
  const believed = plan.believable;
  return (
    <div>
      <div
        className={`rounded-lg border p-5 ${
          believed ? "border-good/45 bg-surface" : "border-critical/45 bg-surface"
        }`}
      >
        <p
          className={`text-[11px] uppercase tracking-[0.16em] ${
            believed ? "text-good" : "text-critical"
          }`}
        >
          {believed ? "Compiled · believed" : "The Decay Wall"}
        </p>
        <h2 className="serif mt-2 text-[22px] font-normal leading-snug text-ink">
          {planHeadline(plan)}
        </h2>

        {believed ? (
          <BelievedBody plan={plan} />
        ) : (
          <WallBody plan={plan} onRevise={onRevise} />
        )}
      </div>

      <button
        type="button"
        onClick={onBack}
        className="mt-4 rounded-md border border-line px-4 py-2 text-[13px] text-ink-2 hover:bg-surface-2"
      >
        ← Change the terms
      </button>
    </div>
  );
}

function BelievedBody({ plan }: { plan: PlanResponse }) {
  const w = plan.wall;
  return (
    <>
      <p className="mt-3 text-[14px] leading-relaxed text-ink-2">
        You reach the objective in about {w.daysToObjective} days, inside the {w.horizonDays}-day
        horizon. At the worst point {maintenanceSharePercent(w)}% of your day goes on keeping what you
        already hold — the rest is progress.
      </p>
      <div className="mt-4 grid grid-cols-3 gap-3">
        <Tile label="Concepts in plan" value={String(w.conceptsInPlan)} />
        <Tile label="Days to objective" value={String(w.daysToObjective)} />
        <Tile label="Peak maintenance" value={`${maintenanceSharePercent(w)}%`} />
      </div>
    </>
  );
}

function WallBody({ plan, onRevise }: { plan: PlanResponse; onRevise: (m: number) => void }) {
  const w = plan.wall;
  const frac = Math.round(reachedFraction(w) * 100);
  const alt = plan.alternative;
  return (
    <>
      <p className="mt-3 text-[14px] leading-relaxed text-ink-2">{wallExplanation(plan)}</p>

      <div className="mt-4">
        <div className="flex items-baseline justify-between text-[11.5px] text-ink-3">
          <span>Reached before the horizon</span>
          <span className="mono">
            {w.conceptsReached}/{w.conceptsInPlan}
          </span>
        </div>
        <div className="mt-1.5 h-2 overflow-hidden rounded-full bg-surface-2">
          <div className="h-full rounded-full bg-critical" style={{ width: `${frac}%` }} />
        </div>
        {runningToStandStill(w) ? (
          <p className="mono mt-2 text-[11px] text-serious">
            {maintenanceSharePercent(w)}% of the peak day is maintenance — running to stand still.
          </p>
        ) : null}
      </div>

      {alt ? (
        <div className="mt-5 rounded-md border border-accent-line bg-[color:var(--accent-soft)] px-4 py-3.5">
          <p className="text-[11px] uppercase tracking-[0.14em] text-accent">
            What PRAXIS believes in instead
          </p>
          <p className="mt-2 text-[13.5px] leading-relaxed text-ink-2">{alternativeOffer(alt)}</p>
          {alt.minutesPerDayThatWouldWork !== null ? (
            <button
              type="button"
              onClick={() => onRevise(alt.minutesPerDayThatWouldWork as number)}
              className="mt-3 rounded-md bg-accent px-4 py-2 text-[13px] font-medium text-accent-ink hover:opacity-95"
            >
              Give it {alt.minutesPerDayThatWouldWork} min/day and recompile
            </button>
          ) : null}
        </div>
      ) : null}
    </>
  );
}

function Tile({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border border-line bg-bg px-3 py-3">
      <div className="text-[10.5px] uppercase tracking-[0.1em] text-ink-4">{label}</div>
      <div className="mono mt-1 text-[19px] text-ink">{value}</div>
    </div>
  );
}
