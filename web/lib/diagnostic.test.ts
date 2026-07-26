import { describe, expect, it } from "vitest";
import type { AnswerResponse, NextItemResponse } from "./api";
import {
  answerIsReady,
  calibrate,
  calibrationVerdict,
  INITIAL_DIAGNOSTIC,
  lastAnswered,
  misconceptions,
  moveOf,
  receiveItem,
  recordAnswer,
  verdictHeadline,
  type AnsweredItem,
  type DiagnosticState,
} from "./diagnostic";

function ask(over: Partial<NextItemResponse> = {}): NextItemResponse {
  return {
    itemId: "item-1",
    stem: "What does a candlestick show that a line chart does not?",
    evidenceType: "Explain",
    theta: 0,
    standardError: 1,
    confidentEnoughToStop: false,
    stopped: false,
    says: null,
    passage: null,
    options: null,
    format: null,
    ...over,
  };
}

function verdict(over: Partial<AnswerResponse> = {}): AnswerResponse {
  return { outcome: "Correct", misconception: null, youSaidYouWereSure: true, andYouWere: "right", ...over };
}

function answered(over: Partial<AnsweredItem> = {}): AnsweredItem {
  return {
    itemId: "i1",
    stem: "Whose roster is wrong?",
    statedConfidence: 0.85,
    outcome: "Wrong",
    youSaidYouWereSure: true,
    andYouWere: "not",
    misconception: "keyword-match-wins",
    thetaBefore: 0,
    thetaAfter: null,
    ...over,
  };
}

/** Drive one full round: receive an item, answer it, then receive the next (which settles θ-after). */
function round(
  state: DiagnosticState,
  item: NextItemResponse,
  confidence: number,
  answer: AnswerResponse,
  nextTheta: number,
): DiagnosticState {
  let s = receiveItem(state, item);
  s = recordAnswer(s, confidence, answer);
  return receiveItem(s, ask({ theta: nextTheta }));
}

/**
 * The real guard, and the reason the verdict screen cannot leak the key: there is no field to
 * leak from. `expectNoAnswerLeak` (in the page's component test) scans rendered prose for known
 * phrasings — worth having, but a closed list of phrases can never be complete; the next round
 * always finds a phrasing the list did not guess. This guard is structural instead: it fails the
 * *build* the moment either wire type grows a field that could carry the key, whatever that field
 * ends up being named — `correctOption`, `answerIndex`, `keyText`, anything shaped like it.
 * This only matters if something actually runs it: `npm run test` (vitest, transpiled through
 * esbuild) never type-checks, and `npm run build` never type-checks `*.test.ts(x)` files, so
 * neither would ever see this guard fire. The "Web   typecheck" stage in `scripts/verify.ps1`
 * (`npm run typecheck`, i.e. `tsc --noEmit` over the whole `web/` tree, tests included) is the one
 * that does — that is what makes this load-bearing rather than decorative.
 */
type KeyShaped<T> = Extract<
  keyof T,
  `${string}orrect${string}` | `${string}nswer${string}` | `${string}key${string}` | `${string}ndex${string}`
>;

describe("structural guard — the key cannot be rendered because it is not here", () => {
  it("AnswerResponse has no field a key could occupy", () => {
    // If a matching field is ever added, `KeyShaped<AnswerResponse>` stops being `never`, the
    // conditional type below resolves to `never`, and assigning `true` to it is a compile error —
    // this line simply stops compiling. Existing fields (`outcome`, `misconception`,
    // `youSaidYouWereSure`, `andYouWere`) do not match any pattern, checked by hand.
    const guard: KeyShaped<AnswerResponse> extends never ? true : never = true;
    expect(guard).toBe(true);
  });

  it("AnsweredItem has no field a key could occupy", () => {
    const guard: KeyShaped<AnsweredItem> extends never ? true : never = true;
    expect(guard).toBe(true);
  });
});

describe("receiveItem", () => {
  it("moves to asking with the item as asked", () => {
    const s = receiveItem(INITIAL_DIAGNOSTIC, ask({ itemId: "x", theta: 0.4 }));
    expect(s.phase).toBe("asking");
    expect(s.current?.itemId).toBe("x");
    expect(s.current?.theta).toBe(0.4);
  });

  it("stops honestly, carrying the reason and no item", () => {
    const s = receiveItem(INITIAL_DIAGNOSTIC, ask({ stopped: true, itemId: null, stem: null, says: "You are tired, and tiredness is not ignorance." }));
    expect(s.phase).toBe("stopped");
    expect(s.current).toBeNull();
    expect(s.stopSays).toContain("tiredness");
  });
});

describe("recordAnswer", () => {
  it("appends the answer with the confidence stated before the verdict, and clears the current item", () => {
    const asked = receiveItem(INITIAL_DIAGNOSTIC, ask());
    const s = recordAnswer(asked, 0.9, verdict());
    expect(s.history).toHaveLength(1);
    expect(s.history[0].statedConfidence).toBe(0.9);
    expect(s.current).toBeNull();
  });

  it("throws rather than invent an entry when there is no current item", () => {
    expect(() => recordAnswer(INITIAL_DIAGNOSTIC, 0.5, verdict())).toThrow();
  });

  it("rests on the verdict instead of racing to the next question", () => {
    const state = {
      phase: "asking" as const,
      current: {
        itemId: "i1",
        stem: "q",
        evidenceType: null,
        theta: 0,
        standardError: 1,
        confidentEnoughToStop: false,
        passage: null,
        options: null,
        format: "MultipleChoice",
      },
      history: [],
      stopSays: null,
    };
    const after = recordAnswer(state, 0.85, {
      outcome: "Wrong",
      misconception: "keyword-match-wins",
      youSaidYouWereSure: true,
      andYouWere: "not",
    });
    expect(after.phase).toBe("marked");
    expect(lastAnswered(after)?.misconception).toBe("keyword-match-wins");
  });
});

describe("verdictHeadline — the one sentence a learner cannot work out for themselves", () => {
  it("names the gap when you were sure and wrong — the sentence PRAXIS exists to say", () => {
    expect(verdictHeadline(answered())).toBe("You said 85% sure, and you were not right.");
  });

  it("does not scold when you were unsure and wrong", () => {
    const h = verdictHeadline(answered({ statedConfidence: 0.55, youSaidYouWereSure: false }));
    expect(h).toContain("55%");
    expect(h).not.toContain("sure");
  });

  it("credits knowledge you are not spending when you were right but doubted it", () => {
    const h = verdictHeadline(
      answered({ outcome: "Correct", andYouWere: "right", statedConfidence: 0.6, youSaidYouWereSure: false }),
    );
    expect(h).toContain("Right");
    expect(h).toContain("60%");
  });

  it("does not over-credit a confident right answer", () => {
    const h = verdictHeadline(answered({ outcome: "Correct", andYouWere: "right" }));
    expect(h).toContain("Right");
  });
});

describe("lastAnswered", () => {
  it("is null before anything is answered", () => {
    expect(lastAnswered(INITIAL_DIAGNOSTIC)).toBeNull();
  });

  it("is the most recently answered item", () => {
    let s = receiveItem(INITIAL_DIAGNOSTIC, ask({ itemId: "a" }));
    s = recordAnswer(s, 0.9, verdict({ andYouWere: "not", outcome: "Wrong", misconception: "m1" }));
    expect(lastAnswered(s)?.itemId).toBe("a");
  });
});

describe("moveOf — the honest difficulty trail", () => {
  it("reads the estimate as rising, falling, or held from θ-before vs θ-after", () => {
    let s = round(INITIAL_DIAGNOSTIC, ask({ theta: 0 }), 0.8, verdict(), 0.6);
    expect(moveOf(s.history[0])).toBe("rose");

    s = round(s, ask({ theta: 0.6 }), 0.4, verdict({ andYouWere: "not", outcome: "Wrong" }), 0.2);
    expect(moveOf(s.history[1])).toBe("fell");
  });
});

describe("misconceptions", () => {
  it("surfaces the named wrong beliefs, de-duplicated", () => {
    let s = round(INITIAL_DIAGNOSTIC, ask({ itemId: "a" }), 0.8, verdict({ andYouWere: "not", outcome: "Wrong", misconception: "confuses-price-with-value" }), 0.1);
    s = round(s, ask({ itemId: "b", theta: 0.1 }), 0.8, verdict({ andYouWere: "not", outcome: "Wrong", misconception: "confuses-price-with-value" }), 0.0);
    expect(misconceptions(s)).toEqual(["confuses-price-with-value"]);
  });
});

describe("calibrate — what you think you know but don't", () => {
  it("is empty and NaN-free before anything is answered", () => {
    const c = calibrate(INITIAL_DIAGNOSTIC);
    expect(c.answered).toBe(0);
    expect(c.accuracy).toBe(0);
    expect(c.overconfidencePoints).toBe(0);
    expect(c.blindspots).toHaveLength(0);
  });

  it("names the blind spots: sure, and wrong", () => {
    const asked = receiveItem(INITIAL_DIAGNOSTIC, ask());
    // Sure and wrong — the definition of a blind spot.
    const s = recordAnswer(asked, 0.9, verdict({ youSaidYouWereSure: true, andYouWere: "not", outcome: "Wrong" }));
    const c = calibrate(s);
    expect(c.blindspots).toHaveLength(1);
    expect(c.underclaimed).toHaveLength(0);
  });

  it("separates underclaimed knowledge: right, but not sure", () => {
    const asked = receiveItem(INITIAL_DIAGNOSTIC, ask());
    const s = recordAnswer(asked, 0.55, verdict({ youSaidYouWereSure: false, andYouWere: "right", outcome: "Correct" }));
    const c = calibrate(s);
    expect(c.underclaimed).toHaveLength(1);
    expect(c.blindspots).toHaveLength(0);
  });

  it("computes overconfidence as confidence minus accuracy in points", () => {
    // Two answers: both stated 0.8 confidence, one right one wrong → accuracy 0.5, confidence 0.8.
    let s = receiveItem(INITIAL_DIAGNOSTIC, ask({ itemId: "a" }));
    s = recordAnswer(s, 0.8, verdict({ youSaidYouWereSure: true, andYouWere: "right", outcome: "Correct" }));
    s = receiveItem(s, ask({ itemId: "b" }));
    s = recordAnswer(s, 0.8, verdict({ youSaidYouWereSure: true, andYouWere: "not", outcome: "Wrong" }));
    const c = calibrate(s);
    expect(c.accuracy).toBeCloseTo(0.5);
    expect(c.averageConfidence).toBeCloseTo(0.8);
    expect(c.overconfidencePoints).toBe(30);
  });
});

describe("answerIsReady", () => {
  it("a fixed-key item needs a choice and ignores typed words", () => {
    expect(answerIsReady({ claim: "", selectedOption: 2 }, "MultipleChoice")).toBe(true);
    expect(answerIsReady({ claim: "some words", selectedOption: null }, "MultipleChoice")).toBe(false);
  });

  it("option zero is a real choice, not a missing one", () => {
    // The bug this test exists to prevent: `if (selectedOption)` treats A as unanswered.
    expect(answerIsReady({ claim: "", selectedOption: 0 }, "MultipleChoice")).toBe(true);
  });

  it("a prose item needs words", () => {
    expect(answerIsReady({ claim: "because however reverses it", selectedOption: null }, "Prose")).toBe(true);
    expect(answerIsReady({ claim: "   ", selectedOption: null }, "Prose")).toBe(false);
  });
});

describe("calibrationVerdict", () => {
  it("leads with the blind spots when there are any", () => {
    const asked = receiveItem(INITIAL_DIAGNOSTIC, ask());
    const s = recordAnswer(asked, 0.95, verdict({ youSaidYouWereSure: true, andYouWere: "not", outcome: "Wrong" }));
    expect(calibrationVerdict(calibrate(s)).toLowerCase()).toContain("blind spot");
  });

  it("credits genuine calibration when confidence and competence are close", () => {
    // Sure-and-right at 0.7, not-sure-and-wrong at 0.3 → accuracy 0.5, confidence 0.5, gap 0.
    let s = receiveItem(INITIAL_DIAGNOSTIC, ask({ itemId: "a" }));
    s = recordAnswer(s, 0.7, verdict({ youSaidYouWereSure: true, andYouWere: "right", outcome: "Correct" }));
    s = receiveItem(s, ask({ itemId: "b" }));
    s = recordAnswer(s, 0.3, verdict({ youSaidYouWereSure: false, andYouWere: "not", outcome: "Wrong" }));
    const c = calibrate(s);
    expect(c.overconfidencePoints).toBe(0);
    expect(calibrationVerdict(c).toLowerCase()).toContain("honesty");
  });
});
