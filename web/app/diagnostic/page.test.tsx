import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import { fireEvent } from "@testing-library/dom";
import type { AnswerResponse, ApiResult, NextItemResponse } from "@/lib/api";

// The page talks to the API only through this seam; mocking it lets us drive a whole adaptive
// session deterministically, without a live backend or a seeded item bank.
const nextItem = vi.fn();
const submitAnswer = vi.fn();
vi.mock("@/lib/api", () => ({
  diagnostics: {
    nextItem: (...a: unknown[]) => nextItem(...a),
    submitAnswer: (...a: unknown[]) => submitAnswer(...a),
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
    ...over,
  };
}
function answer(over: Partial<AnswerResponse>): AnswerResponse {
  return { outcome: "Correct", misconception: null, youSaidYouWereSure: true, andYouWere: "right", ...over };
}

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

describe("DiagnosticPage", () => {
  beforeEach(() => {
    nextItem.mockReset();
    submitAnswer.mockReset();
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

    expect(await screen.findByText("What is order flow?")).toBeInTheDocument();
    await answerCurrent();

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
});
