import { beforeEach, describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { fireEvent } from "@testing-library/dom";
import type { ApiResult, CompetencyRecordResponse } from "@/lib/api";

const me = vi.fn();
vi.mock("@/lib/api", () => ({
  ledger: { me: (...a: unknown[]) => me(...a) },
}));

import LedgerPage from "./page";

function ok<T>(data: T): ApiResult<T> {
  return { ok: true, data };
}
function record(over: Partial<CompetencyRecordResponse> = {}): CompetencyRecordResponse {
  return {
    subject: "learner",
    competencies: [
      {
        conceptId: "c-backprop",
        conceptName: "Backpropagation",
        type: "build",
        gradingMethod: "execution",
        score: 0.95,
        assurance: 1.0,
        evidenceHash: "abc",
      },
      {
        conceptId: "c-tok",
        conceptName: "Tokenization",
        type: "explain",
        gradingMethod: "rubric",
        score: 0.8,
        assurance: 0.85,
        evidenceHash: "def",
      },
    ],
    signature: "c2ln",
    publicKey: "cHVi",
    issuedAt: "2026-03-15T12:00:00Z",
    ...over,
  };
}

describe("LedgerPage", () => {
  beforeEach(() => me.mockReset());

  it("shows each competency with its honest, un-laundered assurance and method", async () => {
    me.mockResolvedValue(ok(record()));
    render(<LedgerPage />);

    expect(await screen.findByText("Backpropagation")).toBeInTheDocument();
    // The built one reads as unfakeable; the rubric one shows its capped trust, not certainty.
    expect(screen.getByText(/ran against hidden tests/i)).toBeInTheDocument();
    expect(screen.getByText(/a model graded it — capped/i)).toBeInTheDocument();
    expect(screen.getByText("85% trust")).toBeInTheDocument();
  });

  it("exports the signed record as a downloaded file", async () => {
    me.mockResolvedValue(ok(record()));
    const createUrl = vi.fn(() => "blob:x");
    const revokeUrl = vi.fn();
    vi.stubGlobal("URL", { ...URL, createObjectURL: createUrl, revokeObjectURL: revokeUrl });
    // The download anchor's real click() would make jsdom attempt a navigation (a warning to stderr,
    // which breaks the PowerShell gate). We are testing that the record is turned into a blob URL, not
    // jsdom's navigation, so no-op the click.
    const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});

    render(<LedgerPage />);
    await screen.findByText("Backpropagation");
    fireEvent.click(screen.getByRole("button", { name: /export/i }));

    expect(createUrl).toHaveBeenCalledOnce();
    click.mockRestore();
    vi.unstubAllGlobals();
  });

  it("says nothing-proved honestly, and even that is a valid signed record", async () => {
    me.mockResolvedValue(ok(record({ competencies: [] })));
    render(<LedgerPage />);
    expect(await screen.findByText(/have not proved anything yet/i)).toBeInTheDocument();
    expect(screen.getByText(/still a real, signed document/i)).toBeInTheDocument();
  });

  it("shows an honest error when the record cannot be issued", async () => {
    me.mockResolvedValue({
      ok: false,
      message: "PRAXIS could not be reached.",
      errors: [],
    } satisfies ApiResult<CompetencyRecordResponse>);
    render(<LedgerPage />);
    expect(await screen.findByRole("alert")).toHaveTextContent(/could not be reached/i);
  });
});
