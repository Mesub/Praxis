/*
  The Ledger — the pure part.

  A Verified Competency Record does not flatter. Each competency carries the *method* that graded it
  and the *assurance* straight from the evidence — so a skill you built and ran shows full, unfakeable
  trust, while one a language model graded shows its capped trust, plainly, and is never laundered
  into certainty (C-6). The presentation's whole job is to keep that honesty visible.
*/

import type { CompetencyRecordResponse } from "./api";

export type MethodPresentation = {
  label: string;
  /** True for methods no model's opinion touches — execution, reality, a keyed match. */
  unfakeable: boolean;
  tone: string;
};

const METHOD: Record<string, MethodPresentation> = {
  execution: { label: "Ran against hidden tests", unfakeable: true, tone: "text-good" },
  reality: { label: "Reality resolved it", unfakeable: true, tone: "text-good" },
  keymatch: { label: "Checked against the key", unfakeable: true, tone: "text-s1" },
  rubric: { label: "A model graded it — capped", unfakeable: false, tone: "text-warn" },
};

export function methodOf(gradingMethod: string): MethodPresentation {
  return (
    METHOD[gradingMethod.toLowerCase()] ?? { label: gradingMethod, unfakeable: false, tone: "text-ink-3" }
  );
}

/** Assurance as a whole percent. */
export function assuranceLabel(assurance: number): string {
  return `${Math.round(assurance * 100)}%`;
}

/** The date a record was true, formatted for a human. */
export function issuedLabel(issuedAt: string): string {
  const d = new Date(issuedAt);
  if (Number.isNaN(d.getTime())) return issuedAt;
  return d.toLocaleDateString(undefined, { day: "numeric", month: "long", year: "numeric" });
}

/**
 * The filename for a downloaded record. Stable and self-describing, so a folder of them from
 * different days sorts and reads sensibly.
 */
export function exportFilename(record: CompetencyRecordResponse): string {
  const date = record.issuedAt.slice(0, 10) || "record";
  return `praxis-competency-record-${date}.json`;
}
