/*
  Today: one card, one action. The whole product answers a single question — "I have fifteen
  minutes, what do I do?" — with one card, never a dashboard. A list is a choice, and a choice handed
  to someone at the hardest moment in learning (starting) is the moment most of them get up again.

  This file is the pure part: how each kind of card presents, and whether it is an action or a rest.
  The `alsoDue` audit trail is data the page reveals only on an explicit "why this?", so the card can
  be *inspected* without ever becoming a menu.
*/

import type { CardKind } from "./api";

export type CardPresentation = {
  /** The short badge label. */
  label: string;
  /** A one-line gloss of what this kind of work is. */
  gloss: string;
  /** Tailwind text-colour token for the accent, kept on-brand. */
  tone: string;
  /** False only for `Nothing` — the honest rest state, which has no action to start. */
  actionable: boolean;
};

const PRESENTATION: Record<CardKind, CardPresentation> = {
  Rescue: {
    label: "Rescue",
    gloss: "Something you hold is slipping. This wins today, and it wins for a reason.",
    tone: "text-critical",
    actionable: true,
  },
  Rep: {
    label: "Rep",
    gloss: "A retrieval on schedule — cheap, and it keeps the whole edifice standing.",
    tone: "text-s2",
    actionable: true,
  },
  Learn: {
    label: "Learn",
    gloss: "The next concept in your plan. The frontier advancing — the one dopamine PRAXIS permits.",
    tone: "text-accent",
    actionable: true,
  },
  Refresh: {
    label: "Refresh",
    gloss: "A concept you let rot far enough that it must be rebuilt, not merely recalled.",
    tone: "text-warn",
    actionable: true,
  },
  Nothing: {
    label: "Nothing due",
    gloss: "Nothing is due and the objective holds. PRAXIS will not invent work to fill the time.",
    tone: "text-ink-3",
    actionable: false,
  },
};

export function presentationOf(kind: CardKind): CardPresentation {
  return PRESENTATION[kind];
}

/** The label on the primary button, given the kind and the minutes on offer. */
export function actionLabel(kind: CardKind, minutes: number): string {
  if (kind === "Nothing") {
    return "Rest — you have earned it";
  }
  return `Start — ${minutes} minutes`;
}
