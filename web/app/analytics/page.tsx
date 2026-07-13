"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { AppShell } from "@/components/AppShell";
import {
  analytics,
  twin,
  type AnalyticsResponse,
  type TwinSummaryResponse,
} from "@/lib/api";
import { calibrationTrend, consistency, minutesLabel, rateLabel, verdictOf } from "@/lib/analytics";
import { calibrationVerdict } from "@/lib/graph";

/*
  Analytics (prototype §10). One number, front and centre, and it is allowed to be bad. Mastery-per-
  Minute (I-10) is the rate of return on a learner's attention — the metric an engagement-optimising
  product can never show, because keeping you on the app longer for the same learning makes it worse.
  When the number is low or zero, PRAXIS says so, and then points at the single smallest next step
  rather than shaming the person for the truth (R-3).
*/

export default function AnalyticsPage() {
  const [mpm, setMpm] = useState<AnalyticsResponse | null>(null);
  const [calibration, setCalibration] = useState<TwinSummaryResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let active = true;
    void (async () => {
      // The headline is required; calibration is a bonus — a Twin hiccup must not blank the page.
      const [mpmRes, twinRes] = await Promise.all([analytics.me(), twin.summary(30)]);
      if (!active) return;
      setLoading(false);
      if (!mpmRes.ok) {
        setError(mpmRes.message);
        return;
      }
      setError(null);
      setMpm(mpmRes.data);
      setCalibration(twinRes.ok ? twinRes.data : null);
    })();
    return () => {
      active = false;
    };
  }, [reloadKey]);

  return (
    <AppShell crumb="Analytics" current="Analytics">
      <div className="mx-auto max-w-2xl">
        <p className="text-[11px] uppercase tracking-[0.18em] text-ink-3">
          The scoreboard PRAXIS keeps on itself
        </p>
        <h1 className="serif mt-2 text-[clamp(22px,2.6vw,30px)] font-normal leading-tight text-ink">
          Was this worth your time?
        </h1>

        <div className="mt-6">
          {loading ? (
            <p className="text-[13.5px] text-ink-3">Adding up your minutes…</p>
          ) : error ? (
            <ErrorPanel message={error} onRetry={() => setReloadKey((k) => k + 1)} />
          ) : mpm ? (
            <Report mpm={mpm} calibration={calibration} />
          ) : null}
        </div>
      </div>
    </AppShell>
  );
}

function Report({
  mpm,
  calibration,
}: {
  mpm: AnalyticsResponse;
  calibration: TwinSummaryResponse | null;
}) {
  const verdict = verdictOf(mpm);
  const trend = calibrationTrend(mpm.calibration);
  const trendTone =
    trend.tone === "good" ? "text-accent" : trend.tone === "bad" ? "text-serious" : "text-ink-2";
  const rhythm = consistency(mpm.consistency);
  return (
    <>
      <section className="rounded-lg border border-line bg-surface p-6">
        <p className="text-[11px] uppercase tracking-[0.14em] text-ink-3">Mastery per hour</p>
        <div className="mt-1 flex items-baseline gap-2">
          <span className="mono text-[clamp(34px,7vw,52px)] leading-none text-accent">
            {rateLabel(mpm.rate)}
          </span>
          {mpm.hasData ? (
            <span className="text-[12px] text-ink-4">verified mastery / hour invested</span>
          ) : null}
        </div>
        <p className="mt-3 text-[14px] leading-relaxed text-ink-2">{verdict.headline}</p>
        {verdict.needsNextStep ? (
          <Link
            href="/"
            className="mt-3 inline-flex rounded-md bg-accent px-4 py-2 text-[13px] font-medium text-accent-ink hover:opacity-95"
          >
            The single smallest next step →
          </Link>
        ) : null}
      </section>

      <div className="mt-4 grid grid-cols-2 gap-3">
        <Tile label="Verified mastery held" value={mpm.masteryCredit.toFixed(2)} />
        <Tile label="Time invested" value={minutesLabel(mpm.minutesInvested)} />
      </div>

      {calibration ? (
        <section className="mt-6 rounded-lg border border-line bg-surface p-5">
          <div className="flex items-baseline justify-between">
            <h2 className="text-[11px] uppercase tracking-[0.14em] text-ink-3">Calibration</h2>
            <span className="mono text-[12px] text-ink-4">
              Brier {calibration.overallBrierScore.toFixed(2)}
            </span>
          </div>
          <p className="mt-2 text-[13.5px] leading-relaxed text-ink-2">
            {calibrationVerdict(calibration)}
          </p>
          {trend.sentence ? (
            <p className={`mt-2 text-[13px] leading-relaxed ${trendTone}`}>{trend.sentence}</p>
          ) : null}
          <Link
            href="/graph"
            className="mt-2 inline-block text-[12.5px] text-accent underline-offset-4 hover:underline"
          >
            See what is decaying →
          </Link>
        </section>
      ) : null}

      {rhythm.sentence ? (
        <section className="mt-4 rounded-lg border border-line bg-surface p-5">
          <h2 className="text-[11px] uppercase tracking-[0.14em] text-ink-3">Your rhythm</h2>
          <p
            className={`mt-2 text-[13.5px] leading-relaxed ${
              rhythm.tone === "good" ? "text-accent" : "text-ink-2"
            }`}
          >
            {rhythm.sentence}
          </p>
        </section>
      ) : null}
    </>
  );
}

function Tile({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border border-line bg-surface px-3 py-3">
      <div className="text-[10.5px] uppercase tracking-[0.1em] text-ink-4">{label}</div>
      <div className="mono mt-1 text-[20px] text-ink">{value}</div>
    </div>
  );
}

function ErrorPanel({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div
      role="alert"
      className="flex flex-col gap-3 rounded-md border border-serious/45 bg-surface px-4 py-4 sm:flex-row sm:items-center sm:justify-between"
    >
      <p className="text-[13.5px] text-ink-2">{message}</p>
      <button
        type="button"
        onClick={onRetry}
        className="shrink-0 rounded-md border border-line px-3 py-1.5 text-[12.5px] text-ink-2 hover:bg-surface-2"
      >
        Try again
      </button>
    </div>
  );
}
