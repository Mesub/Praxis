import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { fireEvent } from "@testing-library/dom";
import type { ApiResult, TodayCardResponse } from "@/lib/api";

const today = vi.fn();
vi.mock("@/lib/api", () => ({
  SESSION_LENGTHS: [5, 10, 15, 25],
  scheduler: { today: (...a: unknown[]) => today(...a) },
}));

import TodayPage from "./page";

function ok<T>(data: T): ApiResult<T> {
  return { ok: true, data };
}
function card(over: Partial<TodayCardResponse> = {}): TodayCardResponse {
  return {
    kind: "Learn",
    conceptId: "c-backprop",
    slug: "backpropagation",
    headline: "Learn Backpropagation",
    because: "Your Chain Rule credit is strongest now and expires in 4 days.",
    minutes: 15,
    alsoDue: [{ conceptId: "c-grad", slug: "gradient-descent", reason: "not yet due", minutes: 10 }],
    ...over,
  };
}

describe("TodayPage", () => {
  beforeEach(() => today.mockReset());

  it("shows one card with its reason and a start action, and reveals the audit only on ask", async () => {
    today.mockResolvedValue(ok(card()));
    render(<TodayPage />);

    expect(await screen.findByRole("heading", { name: /learn backpropagation/i })).toBeInTheDocument();
    expect(screen.getByText(/why now/i)).toBeInTheDocument();

    const start = screen.getByRole("link", { name: /start — 15 minutes/i });
    expect(start).toHaveAttribute("href", expect.stringContaining("/session?concept=c-backprop"));

    // The audit trail is hidden until explicitly asked for — the card is not a menu.
    expect(screen.queryByText("gradient-descent")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /why this and not something else/i }));
    expect(screen.getByText("gradient-descent")).toBeInTheDocument();
  });

  it("says nothing is due out loud, with rest and no work to start", async () => {
    today.mockResolvedValue(
      ok(card({ kind: "Nothing", headline: "Nothing is due. The objective holds.", alsoDue: [] })),
    );
    render(<TodayPage />);

    expect(await screen.findByRole("heading", { name: /nothing is due/i })).toBeInTheDocument();
    expect(screen.getByText(/rest/i)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /start/i })).not.toBeInTheDocument();
  });

  it("shows an honest error and a route to onboarding when there is no card", async () => {
    today.mockResolvedValue({
      ok: false,
      message: "PRAXIS could not be reached. This is a connection problem, not your work.",
      errors: [],
    } satisfies ApiResult<TodayCardResponse>);
    render(<TodayPage />);

    expect(await screen.findByRole("alert")).toHaveTextContent(/connection problem/i);
    expect(screen.getByRole("link", { name: /no objective yet/i })).toBeInTheDocument();
  });
});
