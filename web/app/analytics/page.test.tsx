import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import type { AnalyticsResponse, ApiResult, TwinSummaryResponse } from "@/lib/api";

const me = vi.fn();
const summary = vi.fn();
vi.mock("@/lib/api", () => ({
  analytics: { me: (...a: unknown[]) => me(...a) },
  twin: { summary: (...a: unknown[]) => summary(...a) },
}));

import AnalyticsPage from "./page";

function ok<T>(data: T): ApiResult<T> {
  return { ok: true, data };
}
function mpm(over: Partial<AnalyticsResponse> = {}): AnalyticsResponse {
  // 0.025/min → 1.50/hour; mastery 3.00 over 2 h — kept distinct so each renders uniquely.
  return { rate: 0.025, masteryCredit: 3, minutesInvested: 120, hasData: true, gainedNothing: false, ...over };
}
function twinSummary(): TwinSummaryResponse {
  return {
    conceptsHeld: 5,
    conceptsInProgress: 2,
    conceptsDecaying: 1,
    conceptsLapsed: 0,
    overallBrierScore: 0.18,
    overallConfidenceGap: 0.12,
    calibrationIsMeaningful: true,
    activeMisconceptions: 0,
    decayForecast: [],
  };
}

describe("AnalyticsPage", () => {
  beforeEach(() => {
    me.mockReset();
    summary.mockReset();
    summary.mockResolvedValue(ok(twinSummary()));
  });

  it("puts Mastery-per-Minute front and centre, with calibration alongside", async () => {
    me.mockResolvedValue(ok(mpm()));
    render(<AnalyticsPage />);

    // 0.025/min → 1.50/hour (the rate), distinct from the 3.00 mastery-held tile.
    expect(await screen.findByText("1.50")).toBeInTheDocument();
    expect(screen.getByText(/that is the number that matters/i)).toBeInTheDocument();
    // Calibration reused from the Twin.
    expect(screen.getByText(/12 points ahead/i)).toBeInTheDocument();
  });

  it("says a gained-nothing stretch out loud and offers the next step", async () => {
    me.mockResolvedValue(ok(mpm({ rate: 0, masteryCredit: 0, minutesInvested: 90, gainedNothing: true })));
    render(<AnalyticsPage />);

    expect(await screen.findByText(/did not move/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /single smallest next step/i })).toBeInTheDocument();
  });

  it("calls no-data a blank page, not a zero", async () => {
    me.mockResolvedValue(ok(mpm({ rate: null, masteryCredit: 0, minutesInvested: 0, hasData: false })));
    render(<AnalyticsPage />);

    expect(await screen.findByText(/blank page/i)).toBeInTheDocument();
    expect(screen.getByText("—")).toBeInTheDocument();
  });

  it("still shows the headline when calibration is unavailable", async () => {
    me.mockResolvedValue(ok(mpm()));
    summary.mockResolvedValue({ ok: false, message: "down", errors: [] } satisfies ApiResult<TwinSummaryResponse>);
    render(<AnalyticsPage />);

    expect(await screen.findByText("1.50")).toBeInTheDocument();
    // No calibration section, but the page is not blank.
    expect(screen.queryByText(/^Calibration$/)).not.toBeInTheDocument();
  });

  it("shows an honest error when the number cannot be computed", async () => {
    me.mockResolvedValue({ ok: false, message: "PRAXIS could not be reached.", errors: [] } satisfies ApiResult<AnalyticsResponse>);
    render(<AnalyticsPage />);
    expect(await screen.findByRole("alert")).toHaveTextContent(/could not be reached/i);
  });
});
