import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import { fireEvent } from "@testing-library/dom";
import type { ApiResult, TeachBackResponse } from "@/lib/api";

const submit = vi.fn();
vi.mock("@/lib/api", () => ({
  teachBack: { submit: (...a: unknown[]) => submit(...a) },
}));

import TeachBackPage from "./page";

function ok<T>(data: T): ApiResult<T> {
  return { ok: true, data };
}
function result(over: Partial<TeachBackResponse> = {}): TeachBackResponse {
  return {
    outcome: "Passed",
    score: 0.78,
    misconceptions: [],
    raisedBelief: true,
    ceiling: "A rubric graded this … it can never, by itself, make you held (the Evidence Ceiling).",
    ...over,
  };
}

async function explainAndSubmit() {
  fireEvent.change(await screen.findByLabelText(/your explanation/i), {
    target: { value: "An inverted index maps terms to the documents that contain them." },
  });
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: /explain it back/i }));
  });
}

describe("TeachBackPage", () => {
  beforeEach(() => submit.mockReset());

  it("shows the verdict, the score, and the honest ceiling — never the rubric", async () => {
    submit.mockResolvedValue(ok(result({ misconceptions: ["confuses-index-with-store"] })));
    render(<TeachBackPage />);
    await explainAndSubmit();

    expect(await screen.findByText(/passed the rubric/i)).toBeInTheDocument();
    expect(screen.getByText("78%")).toBeInTheDocument();
    expect(screen.getByText("Evidence accepted")).toBeInTheDocument();
    expect(screen.getByText("confuses-index-with-store")).toBeInTheDocument();
    // The learner is told, explicitly, that the marking scheme is not shown.
    expect(screen.getByText(/you are shown the verdict, not the rubric/i)).toBeInTheDocument();
  });

  it("treats a Pending grade as set-aside, not a zero, and moves no belief", async () => {
    submit.mockResolvedValue(ok(result({ outcome: "Pending", score: null, raisedBelief: false })));
    render(<TeachBackPage />);
    await explainAndSubmit();

    expect(await screen.findByText(/set aside for a human/i)).toBeInTheDocument();
    expect(screen.getByText("—")).toBeInTheDocument();
    expect(screen.getByText("No belief moved")).toBeInTheDocument();
  });

  it("shows an honest error and marks nothing when the grader cannot be reached", async () => {
    submit.mockResolvedValue({
      ok: false,
      message: "PRAXIS could not be reached.",
      errors: [],
    } satisfies ApiResult<TeachBackResponse>);
    render(<TeachBackPage />);
    await explainAndSubmit();

    expect(await screen.findByRole("alert")).toHaveTextContent(/nothing was marked/i);
  });
});
