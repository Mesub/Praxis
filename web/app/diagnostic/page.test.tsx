import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen, waitFor } from "@testing-library/react";
import { fireEvent } from "@testing-library/dom";
import type { AnswerResponse, ApiResult, NextItemResponse } from "@/lib/api";

// The page talks to the API only through this seam; mocking it lets us drive a whole adaptive
// session deterministically, without a live backend or a seeded item bank.
const nextItem = vi.fn();
const submitAnswer = vi.fn();
const ensurePool = vi.fn();
vi.mock("@/lib/api", () => ({
  diagnostics: {
    nextItem: (...a: unknown[]) => nextItem(...a),
    submitAnswer: (...a: unknown[]) => submitAnswer(...a),
    ensurePool: (...a: unknown[]) => ensurePool(...a),
  },
}));

import DiagnosticPage from "./page";

function ok<T>(data: T): ApiResult<T> {
  return { ok: true, data };
}
function item(over: Partial<NextItemResponse>): NextItemResponse {
  return {
    itemId: "a",
    stem: "Q",
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
function answer(over: Partial<AnswerResponse>): AnswerResponse {
  return { outcome: "Correct", misconception: null, youSaidYouWereSure: true, andYouWere: "right", ...over };
}

// Reading-format fixture (M17): a fixed-key item carries its own passage and options, and the
// server marks by comparing two integers — nothing here names which one is right.
const READING_ITEM: NextItemResponse = {
  itemId: "i1",
  stem: "Whose roster is the system most likely to get wrong?",
  evidenceType: "Apply",
  passage: "The system goes live Monday.\n\nHowever, if you swapped a shift, it has no record.",
  options: [
    "Everyone who worked a Saturday shift last month.",
    "Anyone who swapped a shift without telling the office.",
    "All warehouse staff.",
    "Anyone who calls the front desk.",
  ],
  format: "MultipleChoice",
  theta: 0,
  standardError: 1,
  confidentEnoughToStop: false,
  stopped: false,
  says: null,
};

async function answerCurrent() {
  fireEvent.change(await screen.findByLabelText(/your answer/i), {
    target: { value: "my reasoning" },
  });
  // Wrap the click in act(async) so the whole submit → next-item chain settles inside act — no
  // "update not wrapped in act" warnings leaking to stderr (which would break the PowerShell gate).
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: /submit answer/i }));
  });
}

// The verdict (M17, Task 12) rests between answering and the next item — the page no longer races
// there on its own. This advances past it exactly the way a learner does: by asking for it.
async function nextQuestion() {
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: /next question/i }));
  });
}

describe("DiagnosticPage", () => {
  beforeEach(() => {
    nextItem.mockReset();
    submitAnswer.mockReset();
    ensurePool.mockReset();
    ensurePool.mockResolvedValue(ok({ generated: 0, poolSize: 5 }));
  });

  it("drives the adaptive loop and ends on the blind-spot screen", async () => {
    nextItem
      .mockResolvedValueOnce(ok(item({ itemId: "a", stem: "What does a candlestick show?" })))
      .mockResolvedValueOnce(ok(item({ itemId: "b", stem: "What is order flow?", theta: 0.4 })))
      .mockResolvedValueOnce(
        ok(item({ stopped: true, itemId: null, stem: null, theta: 0.4, says: "PRAXIS has what it needs." })),
      );
    submitAnswer
      // First answer: sure, and wrong → a blind spot.
      .mockResolvedValueOnce(ok(answer({ youSaidYouWereSure: true, andYouWere: "not", outcome: "Wrong" })))
      // Second: sure, and right.
      .mockResolvedValueOnce(ok(answer({ youSaidYouWereSure: true, andYouWere: "right", outcome: "Correct" })));

    render(<DiagnosticPage />);

    expect(await screen.findByText("What does a candlestick show?")).toBeInTheDocument();
    await answerCurrent();
    await nextQuestion();

    expect(await screen.findByText("What is order flow?")).toBeInTheDocument();
    await answerCurrent();
    await nextQuestion();

    // The closing screen the whole diagnostic exists to reach.
    expect(await screen.findByRole("heading", { name: /what you think you know but don/i })).toBeInTheDocument();
    // The blind spot is named: the item we were sure about and got wrong.
    expect(screen.getByText("What does a candlestick show?")).toBeInTheDocument();
    expect(screen.getByText(/sure, and wrong/i)).toBeInTheDocument();
  });

  it("shows an honest error and marks nothing when the oracle cannot be reached", async () => {
    nextItem.mockResolvedValueOnce(ok(item({ stem: "A question" })));
    submitAnswer.mockResolvedValueOnce({
      ok: false,
      message: "PRAXIS could not be reached. This is a connection problem, not your work.",
      errors: [],
    } satisfies ApiResult<AnswerResponse>);

    render(<DiagnosticPage />);
    await answerCurrent();

    expect(await screen.findByRole("alert")).toHaveTextContent(/connection problem/i);
    // Still on the question — no verdict was invented from a failed dependency.
    expect(screen.getByText("A question")).toBeInTheDocument();
  });

  it("tops up the pool before asking the first question", async () => {
    const order: string[] = [];
    ensurePool.mockImplementation(async () => {
      order.push("ensurePool");
      return ok({ generated: 0, poolSize: 5 });
    });
    nextItem.mockImplementation(async () => {
      order.push("nextItem");
      return ok(item({ stem: "A question" }));
    });

    render(<DiagnosticPage />);

    expect(await screen.findByText("A question")).toBeInTheDocument();
    expect(order).toEqual(["ensurePool", "nextItem"]);
  });

  it("still asks the question when the pool top-up fails — best-effort, not a gate", async () => {
    ensurePool.mockReset();
    ensurePool.mockRejectedValueOnce(new Error("network"));
    nextItem.mockResolvedValueOnce(ok(item({ stem: "A question" })));

    render(<DiagnosticPage />);

    // nextItem's own refusal is the only honest gate — a failed top-up alone must never
    // produce an error screen.
    expect(await screen.findByText("A question")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("tops up the pool again after each answer, before the next question", async () => {
    nextItem
      .mockResolvedValueOnce(ok(item({ itemId: "a", stem: "First question" })))
      .mockResolvedValueOnce(ok(item({ itemId: "b", stem: "Second question" })));
    submitAnswer.mockResolvedValueOnce(ok(answer({})));

    render(<DiagnosticPage />);

    expect(await screen.findByText("First question")).toBeInTheDocument();
    expect(ensurePool).toHaveBeenCalledTimes(1);

    await answerCurrent();
    await nextQuestion();

    expect(await screen.findByText("Second question")).toBeInTheDocument();
    expect(ensurePool).toHaveBeenCalledTimes(2);
  });

  // ── The reading item (M17): a passage, four radios, and no leak of the key ──────────────
  //
  // The brief's snippet drives these with `userEvent`, a package this project does not depend
  // on (absent from package.json/package-lock.json/node_modules, and package.json is not in the
  // brief's file list). Translated to `fireEvent` — the interaction this file already uses —
  // with the same assertions, wrapping the submit click in `act(async …)` exactly where the
  // file's existing `answerCurrent()` helper does, because that click starts the async
  // submit → next-item chain.

  it("renders the passage with its paragraphs intact, as separate elements", async () => {
    nextItem.mockResolvedValue(ok(READING_ITEM));
    render(<DiagnosticPage />);

    const first = await screen.findByText(/goes live Monday/);
    const second = screen.getByText(/However, if you swapped/);
    expect(first).toBeInTheDocument();
    expect(second).toBeInTheDocument();

    // `getByText` matches a substring anywhere inside one element, so the two assertions above
    // alone would still pass a naive `<p>{passage}</p>` single-block render — the exact
    // regression the `\n\n`-paragraph-break requirement exists to catch. Assert on structure
    // instead: two distinct <p> elements, and exactly two of them in the passage region.
    expect(first).not.toBe(second);
    expect(first.tagName).toBe("P");
    expect(second.tagName).toBe("P");
    expect(screen.getByTestId("passage-text").querySelectorAll("p")).toHaveLength(2);
  });

  it("offers the four choices as radios and nothing to type", async () => {
    nextItem.mockResolvedValue(ok(READING_ITEM));
    render(<DiagnosticPage />);

    expect(await screen.findAllByRole("radio")).toHaveLength(4);
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
  });

  it("will not submit until a choice is made", async () => {
    nextItem.mockResolvedValue(ok(READING_ITEM));
    render(<DiagnosticPage />);

    const submit = await screen.findByRole("button", { name: /submit/i });
    expect(submit).toBeDisabled();

    fireEvent.click(screen.getAllByRole("radio")[0]);
    expect(submit).toBeEnabled();
  });

  it("sends the chosen index and no claim", async () => {
    nextItem.mockResolvedValue(ok(READING_ITEM));
    submitAnswer.mockResolvedValue(
      ok({ outcome: "Wrong", misconception: "keyword-match-wins", youSaidYouWereSure: true, andYouWere: "not" }),
    );
    render(<DiagnosticPage />);

    fireEvent.click((await screen.findAllByRole("radio"))[0]);
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /submit/i }));
    });

    expect(submitAnswer).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({ itemId: "i1", selectedOption: 0 }),
    );
    expect(submitAnswer.mock.calls[0][1].claim).toBeUndefined();
  });

  it("never shows which option was right", async () => {
    nextItem.mockResolvedValue(ok(READING_ITEM));
    submitAnswer.mockResolvedValue(
      ok({ outcome: "Wrong", misconception: "keyword-match-wins", youSaidYouWereSure: true, andYouWere: "not" }),
    );
    render(<DiagnosticPage />);

    fireEvent.click((await screen.findAllByRole("radio"))[0]);
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /submit/i }));
    });

    // The key never reached this browser, so there is nothing here that could name it. The name
    // itself legitimately appears twice once marked (Task 12): once in the verdict's named-belief
    // block, once in the Twin sidebar's running list — hence `findAllByText`, not `findByText`.
    expect((await screen.findAllByText(/keyword-match-wins/)).length).toBeGreaterThan(0);
    expect(screen.queryByText(/correct answer/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/the right answer/i)).not.toBeInTheDocument();
  });

  it("still renders the typing box for a prose item", async () => {
    nextItem.mockResolvedValue(ok({ ...READING_ITEM, passage: null, options: null, format: "Prose" }));
    render(<DiagnosticPage />);

    expect(await screen.findByRole("textbox")).toBeInTheDocument();
    expect(screen.queryAllByRole("radio")).toHaveLength(0);
  });

  // ── The verdict (M17, Task 12): the refusal, said out loud ──────────────────────────────

  it("rests on the verdict and names the belief, without naming the key", async () => {
    nextItem.mockResolvedValue(ok(READING_ITEM));
    submitAnswer.mockResolvedValue(
      ok(answer({ outcome: "Wrong", misconception: "keyword-match-wins", youSaidYouWereSure: true, andYouWere: "not" })),
    );
    render(<DiagnosticPage />);

    fireEvent.click((await screen.findAllByRole("radio"))[0]);
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /submit/i }));
    });

    expect(await screen.findByText(/you were not right/i)).toBeInTheDocument();
    // Named once in the verdict's belief block and again in the Twin sidebar's running list —
    // both are correct per the approved prototype, so this tolerates either or both.
    expect(screen.getAllByText(/keyword-match-wins/).length).toBeGreaterThan(0);
    expect(screen.getByText(/never entered this browser/i)).toBeInTheDocument();

    // The key is not here, on any path, in any casing.
    expect(screen.queryByText(/correct answer/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/the right answer/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/option b/i)).not.toBeInTheDocument();
  });

  it("does not fetch the next item until the learner asks for it", async () => {
    nextItem.mockResolvedValue(ok(READING_ITEM));
    submitAnswer.mockResolvedValue(
      ok(answer({ outcome: "Wrong", misconception: null, youSaidYouWereSure: false, andYouWere: "not" })),
    );
    render(<DiagnosticPage />);

    fireEvent.click((await screen.findAllByRole("radio"))[0]);
    const callsBefore = nextItem.mock.calls.length;
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /submit/i }));
    });

    await screen.findByText(/not right/i);
    expect(nextItem.mock.calls.length).toBe(callsBefore);

    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /next question/i }));
    });
    await waitFor(() => expect(nextItem.mock.calls.length).toBe(callsBefore + 1));
  });

  it("handles a wrong answer that caught no named belief", async () => {
    // 7 of the 15 reading concepts declare no misconceptions. A verdict with nothing to name is a
    // normal outcome, not a broken screen.
    nextItem.mockResolvedValue(ok(READING_ITEM));
    submitAnswer.mockResolvedValue(
      ok(answer({ outcome: "Wrong", misconception: null, youSaidYouWereSure: true, andYouWere: "not" })),
    );
    render(<DiagnosticPage />);

    fireEvent.click((await screen.findAllByRole("radio"))[0]);
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /submit/i }));
    });

    expect(await screen.findByText(/you were not right/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /next question/i })).toBeInTheDocument();
  });
});
