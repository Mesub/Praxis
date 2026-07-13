import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import type { ApiResult, RepsResponse } from "@/lib/api";

const reps = vi.fn();
vi.mock("@/lib/api", () => ({
  memory: { reps: (...a: unknown[]) => reps(...a) },
}));

import RepsPage from "./page";

function ok<T>(data: T): ApiResult<T> {
  return { ok: true, data };
}
function make(over: Partial<RepsResponse> = {}): RepsResponse {
  return {
    due: [
      {
        conceptId: "c-chain-rule",
        slug: "chain-rule",
        urgency: "Rotting",
        retrievability: 0.61,
        expectedMinutes: 4,
        because: "You are down to 61% and falling. Every day you leave this, it is worth less.",
      },
    ],
    lost: [],
    expectedMinutes: 4,
    nothingWorthDoing: false,
    aboutWhatYouLost: null,
    ...over,
  };
}

describe("RepsPage", () => {
  beforeEach(() => reps.mockReset());

  it("lists the queue by worth, with the reason and a review action", async () => {
    reps.mockResolvedValue(ok(make()));
    render(<RepsPage />);

    expect(await screen.findByText("chain-rule")).toBeInTheDocument();
    expect(screen.getByText("Rotting")).toBeInTheDocument();
    expect(screen.getByText(/down to 61% and falling/i)).toBeInTheDocument();
    const review = screen.getByRole("link", { name: /review it/i });
    expect(review).toHaveAttribute("href", expect.stringContaining("/session?concept=c-chain-rule"));
  });

  it("says nothing worth doing plainly, with no debt to bury", async () => {
    reps.mockResolvedValue(ok(make({ due: [], nothingWorthDoing: true, expectedMinutes: 0 })));
    render(<RepsPage />);

    expect(await screen.findByText(/nothing is worth reviewing right now/i)).toBeInTheDocument();
    expect(screen.getByText(/being early is not a virtue/i)).toBeInTheDocument();
  });

  it("shows lost concepts as a re-learn, not hidden and not called a review", async () => {
    reps.mockResolvedValue(
      ok(
        make({
          due: [],
          nothingWorthDoing: false,
          lost: [
            { conceptId: "c-x", slug: "backprop", urgency: "Lost", retrievability: 0.2, expectedMinutes: 12, because: "" },
          ],
          aboutWhatYouLost: "Two concepts decayed past the point a review would work.",
        }),
      ),
    );
    render(<RepsPage />);

    expect(await screen.findByText(/past saving — a re-learn/i)).toBeInTheDocument();
    expect(screen.getByText("backprop")).toBeInTheDocument();
    expect(screen.getByText(/decayed past the point/i)).toBeInTheDocument();
  });

  it("shows an honest error when the queue cannot be read", async () => {
    reps.mockResolvedValue({
      ok: false,
      message: "PRAXIS could not be reached.",
      errors: [],
    } satisfies ApiResult<RepsResponse>);
    render(<RepsPage />);
    expect(await screen.findByRole("alert")).toHaveTextContent(/could not be reached/i);
  });
});
