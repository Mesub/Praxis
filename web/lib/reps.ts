/*
  Reps — the pure part.

  The queue is ordered by what a rep is *worth*, never by what you owe. There is no debt count here
  and there never will be: the number of things you are "behind on" is the number every other product
  makes go down with a snooze button, and PRAXIS does not show it, so there is nothing to bury. Being
  early is not a virtue; the best minutes are the ripe ones, faded enough to bite. And a thing that
  has decayed past saving is called what it is — a re-learn — not dressed up as a quick review.
*/

export type RepPresentation = {
  label: string;
  tone: string;
};

const URGENCY: Record<string, RepPresentation> = {
  ripe: { label: "Ripe", tone: "text-good" },
  rotting: { label: "Rotting", tone: "text-serious" },
  lost: { label: "Lost", tone: "text-critical" },
  fresh: { label: "Fresh", tone: "text-ink-3" },
  neverlearned: { label: "Not learned", tone: "text-ink-4" },
};

export function urgencyOf(urgency: string): RepPresentation {
  return URGENCY[urgency.toLowerCase()] ?? { label: urgency, tone: "text-ink-3" };
}

/** Retrievability as a whole percent — "you'd get this right about N% of the time". */
export function retrievabilityLabel(retrievability: number): string {
  return `${Math.round(retrievability * 100)}%`;
}

/** Expected minutes, rounded for display; a queue never claims a precision it does not have. */
export function minutesLabel(minutes: number): string {
  const m = Math.max(0, Math.round(minutes));
  return `${m} min`;
}
