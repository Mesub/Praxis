import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import type { ApiResult, TwinSummaryResponse } from "@/lib/api";

const summary = vi.fn();
vi.mock("@/lib/api", () => ({
  twin: { summary: (...a: unknown[]) => summary(...a) },
}));

import GraphPage from "./page";

function ok<T>(data: T): ApiResult<T> {
  return { ok: true, data };
}
function make(over: Partial<TwinSummaryResponse> = {}): TwinSummaryResponse {
  return {
    conceptsHeld: 40,
    conceptsInProgress: 8,
    conceptsDecaying: 5,
    conceptsLapsed: 3,
    overallBrierScore: 0.18,
    overallConfidenceGap: 0.12,
    calibrationIsMeaningful: true,
    activeMisconceptions: 2,
    decayForecast: [
      { conceptId: "aaaaaaaa-1111", pKnows: 0.81, retrievability: 0.55, daysUntilDue: 3, alreadyLapsed: false },
    ],
    ...over,
  };
}

describe("GraphPage", () => {
  beforeEach(() => summary.mockReset());

  it("shows the four states with decay, the calibration read, and the decay forecast", async () => {
    summary.mockResolvedValue(ok(make()));
    render(<GraphPage />);

    // The decay number nobody else shows.
    expect(await screen.findByText("Decaying")).toBeInTheDocument();
    expect(screen.getByText("Lapsed")).toBeInTheDocument();
    // Calibration is read honestly — overconfidence in points.
    expect(screen.getByText(/12 points ahead/i)).toBeInTheDocument();
    // The forecast row is present (concept id truncated).
    expect(screen.getByText("aaaaaaaa")).toBeInTheDocument();
    expect(screen.getByText("in 3 days")).toBeInTheDocument();
  });

  it("says nothing is rotting when the forecast is empty", async () => {
    summary.mockResolvedValue(ok(make({ decayForecast: [] })));
    render(<GraphPage />);
    expect(await screen.findByText(/nothing is due to rot/i)).toBeInTheDocument();
  });

  it("points an empty Twin at onboarding", async () => {
    summary.mockResolvedValue(
      ok(make({ conceptsHeld: 0, conceptsInProgress: 0, conceptsDecaying: 0, conceptsLapsed: 0 })),
    );
    render(<GraphPage />);
    expect(await screen.findByText(/nothing in your twin yet/i)).toBeInTheDocument();
  });

  it("shows an honest error when the Twin cannot be read", async () => {
    summary.mockResolvedValue({
      ok: false,
      message: "PRAXIS could not be reached.",
      errors: [],
    } satisfies ApiResult<TwinSummaryResponse>);
    render(<GraphPage />);
    expect(await screen.findByRole("alert")).toHaveTextContent(/could not be reached/i);
  });
});
