/*
  The adaptive diagnostic, as a pure state machine.

  PRAXIS never assumes what you know. It asks, escalating when you are right and de-escalating when
  you are not — it is not trying to give you a good score, it is trying to find the boundary of your
  knowledge and seed the Twin. This file holds the part of that with no pixels: how answers
  accumulate, and how they resolve into the one screen the whole diagnostic exists to show you —
  *what you think you know but don't*.

  The rule that makes calibration mean anything (C-6): confidence is captured **before** the
  outcome. A number collected afterwards measures hindsight and would still look like a number. So
  `statedConfidence` is recorded at answer time and never revised once the verdict lands.
*/

import type { AnswerResponse, NextItemResponse } from "./api";

/** The confidence at or above which PRAXIS treats you as having claimed to be sure (mirrors C# `Diagnosis.ConfidentAt`). */
export const CONFIDENT_AT = 0.7;

/** Which way the ability estimate moved after an answer — the honest version of the "difficulty trail". */
export type Move = "rose" | "fell" | "held";

/** One answered item, frozen at the moment of answering. */
export type AnsweredItem = {
  itemId: string;
  stem: string;
  /** 0..1, stated before the outcome was known. */
  statedConfidence: number;
  outcome: AnswerResponse["outcome"];
  youSaidYouWereSure: boolean;
  andYouWere: AnswerResponse["andYouWere"];
  misconception: string | null;
  /** The ability estimate on the item as it was asked; the "after" is filled when the next estimate lands. */
  thetaBefore: number;
  thetaAfter: number | null;
};

export type CurrentItem = {
  itemId: string;
  stem: string;
  evidenceType: string | null;
  theta: number;
  standardError: number;
  confidentEnoughToStop: boolean;
};

export type Phase = "loading" | "asking" | "stopped";

export type DiagnosticState = {
  phase: Phase;
  current: CurrentItem | null;
  history: readonly AnsweredItem[];
  /** PRAXIS's honest reason for stopping — never "you failed". */
  stopSays: string | null;
};

export const INITIAL_DIAGNOSTIC: DiagnosticState = {
  phase: "loading",
  current: null,
  history: [],
  stopSays: null,
};

/**
 * A `next-item` response arrives. If PRAXIS has stopped asking, we stop — that is a success, not a
 * failure. Either way, the ability estimate that travels with the response resolves the "after θ" of
 * the last answered item, so the estimate trail is honest.
 */
export function receiveItem(state: DiagnosticState, next: NextItemResponse): DiagnosticState {
  const history = settleLastTheta(state.history, next.theta);

  if (next.stopped || next.itemId === null || next.stem === null) {
    return { ...state, phase: "stopped", current: null, history, stopSays: next.says };
  }

  return {
    ...state,
    phase: "asking",
    current: {
      itemId: next.itemId,
      stem: next.stem,
      evidenceType: next.evidenceType,
      theta: next.theta,
      standardError: next.standardError,
      confidentEnoughToStop: next.confidentEnoughToStop,
    },
    history,
    stopSays: null,
  };
}

/**
 * The learner answered the current item and the oracle returned a verdict. We freeze the answer with
 * the confidence they stated *before* this verdict existed. There must be a current item — answering
 * nothing is not a state the UI can reach, and if it did we would rather throw than invent an entry.
 */
export function recordAnswer(
  state: DiagnosticState,
  statedConfidence: number,
  answer: AnswerResponse,
): DiagnosticState {
  if (state.current === null) {
    throw new Error("recordAnswer called with no current item.");
  }

  const answered: AnsweredItem = {
    itemId: state.current.itemId,
    stem: state.current.stem,
    statedConfidence,
    outcome: answer.outcome,
    youSaidYouWereSure: answer.youSaidYouWereSure,
    andYouWere: answer.andYouWere,
    misconception: answer.misconception,
    thetaBefore: state.current.theta,
    thetaAfter: null,
  };

  // The current item is cleared: the UI must fetch the next one, it cannot answer the same item twice.
  return { ...state, current: null, history: [...state.history, answered] };
}

function settleLastTheta(
  history: readonly AnsweredItem[],
  thetaAfter: number,
): readonly AnsweredItem[] {
  if (history.length === 0) {
    return history;
  }
  const last = history[history.length - 1];
  if (last.thetaAfter !== null) {
    return history;
  }
  return [...history.slice(0, -1), { ...last, thetaAfter }];
}

/** Which way the estimate moved on an answered item, once its "after θ" is known. */
export function moveOf(item: AnsweredItem): Move {
  if (item.thetaAfter === null) {
    return "held";
  }
  const delta = item.thetaAfter - item.thetaBefore;
  if (delta > 1e-9) {
    return "rose";
  }
  if (delta < -1e-9) {
    return "fell";
  }
  return "held";
}

/** The named wrong beliefs surfaced so far — the thing a learner cannot get anywhere else. */
export function misconceptions(state: DiagnosticState): readonly string[] {
  const keys = state.history
    .map((h) => h.misconception)
    .filter((m): m is string => m !== null && m.length > 0);
  return Array.from(new Set(keys));
}

// ── The closing screen: what you think you know but don't ──────────────────────

export type Calibration = {
  answered: number;
  /** Answers graded "right". */
  correct: number;
  /** correct / answered, in 0..1. NaN-free: 0 when nothing was answered. */
  accuracy: number;
  /** Mean stated confidence, 0..1. 0 when nothing was answered. */
  averageConfidence: number;
  /**
   * Confidence minus accuracy, in points (−100..+100). Positive means you were more sure than you
   * were right — the gap most likely to cost you. Negative means you know more than you trust.
   */
  overconfidencePoints: number;
  /** Sure, and wrong. This is the list the whole diagnostic exists to show you. */
  blindspots: readonly AnsweredItem[];
  /** Right, but you doubted it — knowledge you are not yet spending. */
  underclaimed: readonly AnsweredItem[];
};

export function calibrate(state: DiagnosticState): Calibration {
  const answered = state.history;
  const n = answered.length;

  if (n === 0) {
    return {
      answered: 0,
      correct: 0,
      accuracy: 0,
      averageConfidence: 0,
      overconfidencePoints: 0,
      blindspots: [],
      underclaimed: [],
    };
  }

  const correct = answered.filter((a) => a.andYouWere === "right").length;
  const accuracy = correct / n;
  const averageConfidence = answered.reduce((s, a) => s + a.statedConfidence, 0) / n;
  const overconfidencePoints = Math.round((averageConfidence - accuracy) * 100);

  const blindspots = answered.filter((a) => a.youSaidYouWereSure && a.andYouWere === "not");
  const underclaimed = answered.filter((a) => !a.youSaidYouWereSure && a.andYouWere === "right");

  return {
    answered: n,
    correct,
    accuracy,
    averageConfidence,
    overconfidencePoints,
    blindspots,
    underclaimed,
  };
}

/** PRAXIS's one honest sentence about the gap — the most useful thing it can say to a person. */
export function calibrationVerdict(c: Calibration): string {
  if (c.answered === 0) {
    return "Nothing answered yet — there is nothing to be honest about.";
  }
  if (c.blindspots.length > 0) {
    const n = c.blindspots.length;
    return `You were sure and wrong ${n} ${n === 1 ? "time" : "times"}. That is not a knowledge gap — it is a blind spot, and it is the thing most likely to cost you.`;
  }
  if (c.overconfidencePoints >= 10) {
    return "Your confidence runs ahead of your competence. The knowledge is real; the certainty is borrowed.";
  }
  if (c.overconfidencePoints <= -10) {
    return "You know more than you trust yourself to. You are underclaiming — spend it.";
  }
  return "Your confidence and your competence are close. That honesty is rarer than either alone.";
}
