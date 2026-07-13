import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import { fireEvent } from "@testing-library/dom";
import type {
  ApiResult,
  ObjectiveResponse,
  PlanResponse,
  WallResponse,
} from "@/lib/api";

const setObjective = vi.fn();
const plan = vi.fn();
vi.mock("@/lib/api", () => ({
  compiler: {
    setObjective: (...a: unknown[]) => setObjective(...a),
    plan: (...a: unknown[]) => plan(...a),
  },
}));

import ContractPage from "./page";

function ok<T>(data: T): ApiResult<T> {
  return { ok: true, data };
}
function wall(over: Partial<WallResponse> = {}): WallResponse {
  return {
    reachable: false,
    conceptsReached: 22,
    conceptsInPlan: 40,
    daysToObjective: 300,
    horizonDays: 200,
    peakMaintenanceMinutesPerDay: 30,
    maintenanceShareOfDay: 0.9,
    ...over,
  };
}
function makePlan(over: Partial<PlanResponse> = {}): PlanResponse {
  return {
    objective: "obj",
    steps: [],
    totalMinutes: 5000,
    conceptsAlreadyHeld: 3,
    conceptsNeedingRefresh: 1,
    daysAtStatedPace: 300,
    believable: false,
    wall: wall(),
    alternative: {
      steps: [],
      minutesPerDayThatWouldWork: 60,
      conceptsReachable: 22,
      conceptsInObjective: 40,
    },
    ...over,
  };
}

const objResponse: ObjectiveResponse = {
  id: "o1",
  conceptId: "c1",
  minutesPerDay: 34,
  deadline: null,
  replaced: 0,
};

async function clickCompile() {
  await act(async () => {
    fireEvent.click(screen.getByRole("button", { name: /sign the contract & compile/i }));
  });
}

describe("ContractPage", () => {
  beforeEach(() => {
    setObjective.mockReset();
    plan.mockReset();
  });

  it("hits the Decay Wall, shows the forward simulation, and offers the narrower objective", async () => {
    setObjective.mockResolvedValue(ok(objResponse));
    plan.mockResolvedValueOnce(ok(makePlan()));

    render(<ContractPage />);
    await clickCompile();

    expect(await screen.findByText(/the decay wall/i)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /does not believe/i })).toBeInTheDocument();
    // The forward simulation is shown, not hidden behind the verdict.
    expect(screen.getByText("22/40")).toBeInTheDocument();
    // The mono callout that quantifies it — unique on the page.
    expect(screen.getByText(/% of the peak day is maintenance/i)).toBeInTheDocument();
    // And the offer: the minutes that would buy the whole thing.
    expect(screen.getByText(/60 minutes a day/i)).toBeInTheDocument();
  });

  it("recompiles when the learner takes the pace that would work, and can then be believed", async () => {
    setObjective.mockResolvedValue(ok(objResponse));
    plan
      .mockResolvedValueOnce(ok(makePlan()))
      .mockResolvedValueOnce(ok(makePlan({ believable: true, alternative: null })));

    render(<ContractPage />);
    await clickCompile();

    await act(async () => {
      fireEvent.click(await screen.findByRole("button", { name: /give it 60 min\/day/i }));
    });

    expect(await screen.findByText(/compiled · believed/i)).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /believes this/i })).toBeInTheDocument();
    expect(plan).toHaveBeenCalledTimes(2);
  });

  it("surfaces an honest error and stays on the form when the compiler cannot be reached", async () => {
    setObjective.mockResolvedValueOnce({
      ok: false,
      message: "PRAXIS could not be reached. This is a connection problem, not your work.",
      errors: [],
    } satisfies ApiResult<ObjectiveResponse>);

    render(<ContractPage />);
    await clickCompile();

    expect(await screen.findByRole("alert")).toHaveTextContent(/connection problem/i);
    expect(screen.getByRole("button", { name: /sign the contract & compile/i })).toBeInTheDocument();
    expect(plan).not.toHaveBeenCalled();
  });
});
