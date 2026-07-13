import { describe, expect, it } from "vitest";
import { render, screen } from "@testing-library/react";
import { fireEvent } from "@testing-library/dom";
import OnboardingPage from "./page";
import { HARD_TRUTH } from "@/lib/onboarding";

describe("OnboardingPage", () => {
  it("leads with the hard truth as the headline", () => {
    render(<OnboardingPage />);
    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe(HARD_TRUTH);
  });

  it("starts with Continue disabled — you cannot drift in", () => {
    render(<OnboardingPage />);
    expect(screen.getByRole("button", { name: /continue to the diagnostic/i })).toBeDisabled();
  });

  it("stays disabled with only a choice, or only the affirmation", () => {
    render(<OnboardingPage />);
    const cta = screen.getByRole("button", { name: /continue to the diagnostic/i });

    // Choose an Objective, but do not affirm.
    fireEvent.click(screen.getByRole("button", { name: /ai engineer/i }));
    expect(cta).toBeDisabled();

    // Undo the choice, affirm instead.
    fireEvent.click(screen.getByRole("button", { name: /ai engineer/i }));
    fireEvent.click(screen.getByRole("switch"));
    expect(cta).toBeDisabled();
  });

  it("enables Continue only after both a choice and the affirmation, then locks the Objective", () => {
    render(<OnboardingPage />);
    const cta = screen.getByRole("button", { name: /continue to the diagnostic/i });

    fireEvent.click(screen.getByRole("switch"));
    fireEvent.click(screen.getByRole("button", { name: /ai engineer/i }));
    expect(cta).toBeEnabled();

    fireEvent.click(cta);
    expect(screen.getByRole("status").textContent).toContain("AI Engineer");
  });
});
