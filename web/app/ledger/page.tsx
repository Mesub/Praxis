"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { AppShell } from "@/components/AppShell";
import {
  ledger,
  type CompetencyRecordResponse,
  type VerifiedCompetencyResponse,
} from "@/lib/api";
import { assuranceLabel, exportFilename, issuedLabel, methodOf } from "@/lib/ledger";

/*
  The Ledger (prototype §9). Everything you have proved, and the export. The record is the honest one:
  each competency shows how it was graded and the assurance straight from the evidence, so a skill you
  built and ran reads as fully trusted and one a model graded shows its capped trust, never laundered
  into certainty (C-6). The export downloads the *signed* record — the document an employer verifies
  offline against PRAXIS's published key, needing nothing from PRAXIS to do it.
*/

export default function LedgerPage() {
  const [record, setRecord] = useState<CompetencyRecordResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let active = true;
    void (async () => {
      const res = await ledger.me();
      if (!active) return;
      setLoading(false);
      if (!res.ok) {
        setError(res.message);
        return;
      }
      setError(null);
      setRecord(res.data);
    })();
    return () => {
      active = false;
    };
  }, [reloadKey]);

  function download() {
    if (record === null || typeof URL.createObjectURL !== "function") return;
    const blob = new Blob([JSON.stringify(record, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = exportFilename(record);
    anchor.click();
    URL.revokeObjectURL(url);
  }

  return (
    <AppShell crumb="The Ledger" current="The Ledger">
      <div className="mx-auto max-w-2xl">
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="text-[11px] uppercase tracking-[0.18em] text-ink-3">Verified, and yours</p>
            <h1 className="serif mt-2 text-[clamp(22px,2.6vw,30px)] font-normal leading-tight text-ink">
              Everything you have proved.
            </h1>
          </div>
          {record ? (
            <button
              type="button"
              onClick={download}
              className="shrink-0 rounded-md bg-accent px-4 py-2 text-[13px] font-medium text-accent-ink hover:opacity-95"
            >
              Export ↓
            </button>
          ) : null}
        </div>

        <p className="mt-3 max-w-prose text-[14px] leading-relaxed text-ink-2">
          A signed record of what you hold — issued fresh, true the moment it is signed. Each
          competency carries how it was graded and how far it can be trusted, honestly. Anyone you hand
          it to can verify it offline against PRAXIS&rsquo;s published key.
        </p>

        <div className="mt-6">
          {loading ? (
            <p className="text-[13.5px] text-ink-3">Signing your record…</p>
          ) : error ? (
            <ErrorPanel message={error} onRetry={() => setReloadKey((k) => k + 1)} />
          ) : record ? (
            <Record record={record} />
          ) : null}
        </div>
      </div>
    </AppShell>
  );
}

function Record({ record }: { record: CompetencyRecordResponse }) {
  if (record.competencies.length === 0) {
    return (
      <div className="rounded-lg border border-line bg-surface px-4 py-6 text-center">
        <p className="text-[14px] text-ink">You have not proved anything yet — and that is honest.</p>
        <p className="mt-1.5 text-[12.5px] text-ink-3">
          Mastery moves on evidence, not on time spent. This record fills as you hold concepts. The
          empty one you could export right now is still a real, signed document — it just says so.
        </p>
        <Link
          href="/"
          className="mt-3 inline-flex rounded-md border border-line px-4 py-2 text-[13px] text-ink-2 hover:bg-surface-2"
        >
          What do I do next? →
        </Link>
      </div>
    );
  }

  return (
    <>
      <p className="text-[11.5px] text-ink-4">
        {record.competencies.length} competenc{record.competencies.length === 1 ? "y" : "ies"} ·
        signed {issuedLabel(record.issuedAt)}
      </p>
      <ul className="mt-2 flex flex-col gap-2">
        {record.competencies.map((c) => (
          <CompetencyRow key={c.conceptId} competency={c} />
        ))}
      </ul>
      <p className="mt-4 text-[11.5px] leading-relaxed text-ink-4">
        The export is the whole signed document, including PRAXIS&rsquo;s signature and public key. A
        verifier compares that key against the one PRAXIS publishes independently — a record that
        carried only its own key would prove nothing.
      </p>
    </>
  );
}

function CompetencyRow({ competency }: { competency: VerifiedCompetencyResponse }) {
  const method = methodOf(competency.gradingMethod);
  return (
    <li className="rounded-lg border border-line bg-surface p-4">
      <div className="flex items-baseline justify-between gap-3">
        <span className="serif text-[16px] text-ink">{competency.conceptName}</span>
        <span className="mono shrink-0 text-[12px] text-ink-4">
          {assuranceLabel(competency.assurance)} trust
        </span>
      </div>
      <div className="mt-1.5 flex items-center gap-2">
        <span className={`text-[11px] uppercase tracking-[0.1em] ${method.tone}`}>{method.label}</span>
        {method.unfakeable ? (
          <span className="text-[10.5px] text-ink-4">· no model&rsquo;s opinion involved</span>
        ) : null}
      </div>
    </li>
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
