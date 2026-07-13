"use client";

import { useMemo, useState } from "react";
import {
  HARD_TRUTH,
  INITIAL_ONBOARDING,
  onboardingReady,
  searchObjectives,
  type Difficulty,
  type Objective,
} from "@/lib/onboarding";

/*
  The Become screen (prototype §1). No rail, no console chrome — you have not entered PRAXIS yet.
  You are deciding whether to. The screen leads with the hard truth, makes you pick a genuinely hard
  target, and refuses to let you continue until you have said, in a click that was never pre-checked,
  that you understand what you asked for. That refusal is invention R-3 rendered in the UI.
*/

const DIFFICULTY_STYLE: Record<Difficulty, string> = {
  Serious: "border-warn/40 text-warn",
  Hard: "border-serious/40 text-serious",
  Punishing: "border-critical/45 text-critical",
};

export default function OnboardingPage() {
  const [query, setQuery] = useState("");
  const [state, setState] = useState(INITIAL_ONBOARDING);
  const [committed, setCommitted] = useState(false);

  const results = useMemo(() => searchObjectives(query), [query]);
  const ready = onboardingReady(state);
  const chosen = results.find((o) => o.id === state.selectedId)
    ?? searchObjectives("").find((o) => o.id === state.selectedId);

  return (
    <div className="min-h-dvh px-4 py-10 md:py-16">
      <div className="mx-auto max-w-3xl">
        <div className="serif text-[19px] font-semibold tracking-[0.14em] text-ink">PRAXIS</div>

        <p className="mt-10 text-[11px] uppercase tracking-[0.2em] text-ink-3">
          Onboarding · The Objective
        </p>
        <h1 className="serif mt-3 text-[clamp(28px,4.5vw,44px)] font-normal leading-[1.08] text-ink">
          {HARD_TRUTH}
        </h1>
        <p className="mt-5 max-w-prose text-[15px] leading-relaxed text-ink-2">
          Not what you want to read. Not what you want to watch. An <b className="text-ink">Objective</b>{" "}
          is a build target — PRAXIS compiles the shortest honest path to it from what you already
          hold. There is no catalog to browse and no streak to protect. If you want to be told you are
          doing fine, this is the wrong place to be.
        </p>

        <Affirmation
          accepted={state.acknowledgedHardTruth}
          onToggle={() =>
            setState((s) => ({ ...s, acknowledgedHardTruth: !s.acknowledgedHardTruth }))
          }
        />

        <hr className="my-9 border-line" />

        <h2 className="serif text-[22px] font-normal text-ink">What do you want to become?</h2>
        <div className="mt-4">
          <label htmlFor="objsearch" className="sr-only">
            Search Objectives
          </label>
          <div className="flex items-center gap-2.5 rounded-md border border-line bg-surface px-3">
            <SearchGlyph />
            <input
              id="objsearch"
              type="search"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder='Search — "AI Engineer", "Radiologist", "Options Trader"…'
              className="w-full bg-transparent py-2.5 text-[14px] text-ink placeholder:text-ink-4 focus:outline-none"
            />
          </div>
        </div>

        <ul className="mt-4 grid gap-3 sm:grid-cols-2">
          {results.map((o) => (
            <li key={o.id}>
              <ObjectiveCard
                objective={o}
                selected={state.selectedId === o.id}
                onSelect={() =>
                  setState((s) => ({ ...s, selectedId: s.selectedId === o.id ? null : o.id }))
                }
              />
            </li>
          ))}
          {results.length === 0 ? (
            <li className="sm:col-span-2 rounded-md border border-line bg-surface px-4 py-6 text-[13.5px] text-ink-3">
              No Objective matches that yet. Phase 0 ships a curated few — the full role ontology is
              seeded later. Try a broader word.
            </li>
          ) : null}
        </ul>

        <p className="mt-4 text-[12.5px] text-ink-4">
          Every Objective is a role ontology, not a course list. The dependency graph is shared; only
          your cache of what you already hold differs.
        </p>

        <div className="mt-9 flex flex-col gap-3 border-t border-line pt-6 sm:flex-row sm:items-center sm:justify-between">
          <p className="text-[13px] text-ink-3">
            {ready
              ? "You have chosen, and you have said you mean it."
              : "Choose an Objective and affirm the truth above to continue."}
          </p>
          <button
            type="button"
            disabled={!ready}
            aria-disabled={!ready}
            onClick={() => setCommitted(true)}
            className="inline-flex items-center justify-center rounded-md bg-accent px-5 py-2.5 text-[13.5px] font-medium text-accent-ink transition-[opacity,transform] duration-[220ms] ease-brand hover:opacity-95 disabled:cursor-not-allowed disabled:bg-surface-2 disabled:text-ink-4"
          >
            Continue to the Diagnostic
          </button>
        </div>

        {committed && chosen ? (
          <div
            role="status"
            className="mt-5 rounded-md border border-accent-line bg-[color:var(--accent-soft)] px-4 py-3.5 text-[13.5px] text-ink-2"
          >
            <b className="text-ink">Objective locked — {chosen.title}.</b> Next, the Diagnostic finds
            what you already hold, so PRAXIS compiles from the truth and not from your optimism.
          </div>
        ) : null}
      </div>
    </div>
  );
}

function Affirmation({ accepted, onToggle }: { accepted: boolean; onToggle: () => void }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={accepted}
      onClick={onToggle}
      className={`mt-6 flex w-full items-center gap-3.5 rounded-md border px-4 py-3.5 text-left transition-[border-color,background-color] duration-[220ms] ease-brand ${
        accepted
          ? "border-accent-line bg-[color:var(--accent-soft)]"
          : "border-line bg-surface hover:border-ink-4"
      }`}
    >
      <span
        aria-hidden="true"
        className={`grid h-5 w-5 shrink-0 place-items-center rounded-[5px] border ${
          accepted ? "border-accent bg-accent text-accent-ink" : "border-ink-4"
        }`}
      >
        {accepted ? <CheckGlyph /> : null}
      </span>
      <span className="text-[13.5px] leading-snug text-ink-2">
        <b className="text-ink">I want to be told when I am failing.</b> I understand PRAXIS grades me
        on evidence, not effort, and will say so when I am off track.
      </span>
    </button>
  );
}

function ObjectiveCard({
  objective,
  selected,
  onSelect,
}: {
  objective: Objective;
  selected: boolean;
  onSelect: () => void;
}) {
  return (
    <button
      type="button"
      aria-pressed={selected}
      onClick={onSelect}
      className={`flex h-full w-full flex-col rounded-md border p-4 text-left transition-[border-color,background-color] duration-[220ms] ease-brand ${
        selected
          ? "border-accent-line bg-surface-2"
          : "border-line bg-surface hover:border-ink-4 hover:bg-surface-2"
      }`}
    >
      <div className="flex items-start justify-between gap-3">
        <h3 className="serif text-[17px] font-normal text-ink">{objective.title}</h3>
        <span
          className={`shrink-0 rounded-full border px-2 py-0.5 text-[10.5px] uppercase tracking-[0.08em] ${DIFFICULTY_STYLE[objective.difficulty]}`}
        >
          {objective.difficulty}
        </span>
      </div>
      <p className="mt-2 text-[13px] leading-relaxed text-ink-2">{objective.role}</p>
      <p className="mt-3 text-[12px] leading-relaxed text-ink-3">{objective.cost}</p>
    </button>
  );
}

function SearchGlyph() {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 16 16"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.5"
      className="shrink-0 text-ink-3"
      aria-hidden="true"
    >
      <circle cx="7" cy="7" r="4.5" />
      <path d="m10.5 10.5 3 3" strokeLinecap="round" />
    </svg>
  );
}

function CheckGlyph() {
  return (
    <svg width="12" height="12" viewBox="0 0 12 12" fill="none" aria-hidden="true">
      <path
        d="M2.5 6.2 5 8.7l4.5-5"
        stroke="currentColor"
        strokeWidth="1.8"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}
