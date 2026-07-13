import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import { fireEvent } from "@testing-library/dom";
import type {
  ApiResult,
  CommitPredictionResponse,
  LoopsBoardResponse,
  ResolveResponse,
} from "@/lib/api";

const board = vi.fn();
const commit = vi.fn();
const resolve = vi.fn();
vi.mock("@/lib/api", () => ({
  reality: {
    board: (...a: unknown[]) => board(...a),
    commit: (...a: unknown[]) => commit(...a),
    resolve: (...a: unknown[]) => resolve(...a),
  },
}));

import LoopsPage from "./page";

function ok<T>(data: T): ApiResult<T> {
  return { ok: true, data };
}
function makeBoard(over: Partial<LoopsBoardResponse> = {}): LoopsBoardResponse {
  return {
    open: [
      {
        loopId: "loop-1",
        claim: "prints 42",
        confidence: 0.9,
        resolvesAt: "2026-01-01T00:00:00Z",
        horizonDays: 0,
        resolvesToday: true,
        isOverdue: false,
      },
    ],
    resolvingToday: [],
    overdue: [],
    resolved: 0,
    forfeited: 0,
    coverage: null,
    headline: "You have 1 open prediction.",
    ...over,
  };
}

describe("LoopsPage", () => {
  beforeEach(() => {
    board.mockReset();
    commit.mockReset();
    resolve.mockReset();
  });

  it("shows the board and resolves a prediction against reality", async () => {
    board.mockResolvedValue(ok(makeBoard()));
    resolve.mockResolvedValue(
      ok({
        loopId: "loop-1",
        correct: false,
        brierScore: 0.81,
        predicted: "42",
        groundTruth: "7",
        confidence: 0.9,
      } satisfies ResolveResponse),
    );

    render(<LoopsPage />);

    expect(await screen.findByText("prints 42")).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /resolve against reality/i }));
    });

    // Sure and wrong — named as the most expensive kind, with the ground truth revealed.
    expect(await screen.findByText("Wrong")).toBeInTheDocument();
    expect(screen.getByText(/most expensive/i)).toBeInTheDocument();
    expect(screen.getByText(/it actually did: 7/i)).toBeInTheDocument();
  });

  it("keeps the loop open and marks nothing when the sandbox is down", async () => {
    board.mockResolvedValue(ok(makeBoard()));
    resolve.mockResolvedValue({
      ok: false,
      message: "PRAXIS could not run that.",
      errors: [],
    } satisfies ApiResult<ResolveResponse>);

    render(<LoopsPage />);
    await screen.findByText("prints 42");
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /resolve against reality/i }));
    });

    expect(await screen.findByText(/nothing was marked/i)).toBeInTheDocument();
    // No verdict rendered.
    expect(screen.queryByText("Wrong")).not.toBeInTheDocument();
  });

  it("commits a new prediction, frozen with its confidence", async () => {
    board.mockResolvedValue(ok(makeBoard({ open: [], headline: "No open predictions." })));
    commit.mockResolvedValue(
      ok({ loopId: "l2", kind: "Output", answer: "4", confidence: 0.7 } satisfies CommitPredictionResponse),
    );

    render(<LoopsPage />);
    await screen.findByText(/make a prediction/i);

    fireEvent.change(screen.getByLabelText(/the program/i), { target: { value: "print(2+2)" } });
    fireEvent.change(screen.getByLabelText(/your prediction/i), { target: { value: "4" } });
    await act(async () => {
      fireEvent.click(screen.getByRole("button", { name: /commit — no takebacks/i }));
    });

    expect(commit).toHaveBeenCalledWith(
      expect.objectContaining({ kind: "Output", code: "print(2+2)", answer: "4", confidence: 0.7 }),
    );
    expect(await screen.findByText(/it is on the board now/i)).toBeInTheDocument();
  });

  it("shows an honest error when the board cannot be read", async () => {
    board.mockResolvedValue({
      ok: false,
      message: "PRAXIS could not be reached.",
      errors: [],
    } satisfies ApiResult<LoopsBoardResponse>);

    render(<LoopsPage />);
    expect(await screen.findByRole("alert")).toHaveTextContent(/could not be reached/i);
  });
});
