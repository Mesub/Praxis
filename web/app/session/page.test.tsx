import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import { fireEvent } from "@testing-library/dom";
import type { ApiResult, SessionResponse, TurnResponse } from "@/lib/api";

const start = vi.fn();
const hint = vi.fn();
const stuckNote = vi.fn();
vi.mock("@/lib/api", () => ({
  tutor: {
    start: (...a: unknown[]) => start(...a),
    hint: (...a: unknown[]) => hint(...a),
    stuckNote: (...a: unknown[]) => stuckNote(...a),
  },
}));

import SessionPage from "./page";

function ok<T>(data: T): ApiResult<T> {
  return { ok: true, data };
}
function session(over: Partial<SessionResponse> = {}): SessionResponse {
  return {
    sessionId: "s1",
    conceptId: "c1",
    state: "Active",
    outcome: null,
    budgetRemaining: 40,
    nextRungCost: 0,
    nextRung: "Orient",
    hintsTaken: 0,
    produced: 0,
    madeSomething: false,
    awaitingYou: false,
    ...over,
  };
}
function turn(tutorText: string, rung: string, cost: number, next: SessionResponse): TurnResponse {
  return { tutor: tutorText, rung, cost, session: next };
}

describe("SessionPage", () => {
  beforeEach(() => {
    start.mockReset();
    hint.mockReset();
    stuckNote.mockReset();
  });

  it("takes the free rung, then a paid one, and the budget ticks down", async () => {
    start.mockResolvedValue(ok(session()));
    hint
      .mockResolvedValueOnce(
        ok(turn("Restate the goal.", "Orient", 0, session({ nextRung: "Probe", nextRungCost: 6, hintsTaken: 1 }))),
      )
      .mockResolvedValueOnce(
        ok(turn("What changes if x grows?", "Probe", 6, session({ budgetRemaining: 34, nextRung: "Analogy", nextRungCost: 14, hintsTaken: 2 }))),
      );

    render(<SessionPage />);

    // The budget is visible from the start.
    const meter = await screen.findByRole("progressbar");
    expect(meter).toHaveAttribute("aria-valuenow", "40");

    // The free rung costs nothing and needs no reply.
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /take the orient hint — free/i }));
    });
    expect(await screen.findByText("Restate the goal.")).toBeInTheDocument();
    expect(meter).toHaveAttribute("aria-valuenow", "40");

    // A paid rung needs the learner to say something first (LR-3), then it spends.
    fireEvent.change(screen.getByLabelText(/your thinking so far/i), {
      target: { value: "I think it grows without bound" },
    });
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /take the probe hint — 6/i }));
    });
    expect(await screen.findByText("What changes if x grows?")).toBeInTheDocument();
    expect(screen.getByText("I think it grows without bound")).toBeInTheDocument();
    // Ticked down: 40 → 34.
    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuenow", "34");
  });

  it("cannot advance a paid rung without saying something", async () => {
    start.mockResolvedValue(ok(session({ nextRung: "Probe", nextRungCost: 6, hintsTaken: 1 })));
    render(<SessionPage />);
    expect(await screen.findByRole("button", { name: /take the probe hint — 6/i })).toBeDisabled();
  });

  it("shows the stuck-note when the budget can't buy the next rung, and records it", async () => {
    start.mockResolvedValue(ok(session({ budgetRemaining: 5, nextRung: "Probe", nextRungCost: 6, hintsTaken: 3 })));
    stuckNote.mockResolvedValue(ok(session({ budgetRemaining: 5, state: "Ended" })));

    render(<SessionPage />);

    expect(await screen.findByText(/where exactly does your reasoning break/i)).toBeInTheDocument();
    // The answer is still not on offer.
    expect(screen.queryByRole("button", { name: /take the .* hint/i })).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/where exactly does your reasoning break/i), {
      target: { value: "I understood the base case but not the inductive step at all" },
    });
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /record the note & reschedule/i }));
    });
    expect(await screen.findByText(/not wasted/i)).toBeInTheDocument();
  });

  it("surfaces an honest error when the session cannot start", async () => {
    start.mockResolvedValueOnce({
      ok: false,
      message: "PRAXIS could not be reached. This is a connection problem, not your work.",
      errors: [],
    } satisfies ApiResult<SessionResponse>);
    render(<SessionPage />);
    expect(await screen.findByRole("alert")).toHaveTextContent(/connection problem/i);
  });
});
