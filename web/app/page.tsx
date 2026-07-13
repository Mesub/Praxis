import { AppShell } from "@/components/AppShell";

export default function Home() {
  return (
    <AppShell crumb="Today" current="Today">
      <div className="mx-auto max-w-2xl">
        <p className="text-[11px] uppercase tracking-[0.18em] text-ink-3">The console</p>
        <h1 className="serif mt-2 text-3xl leading-tight text-ink">
          PRAXIS will tell you when you are failing.
        </h1>
        <p className="mt-4 max-w-prose text-ink-2">
          This is the Phase&nbsp;0 shell. The design system is in place — the brass accent, the
          serif display, the tabular figures — and the pages land next: the Diagnostic, the
          Contract with its Decay&nbsp;Wall, Today, the Session, Teach-back, the Graph, Reps,
          Loops, and the Ledger.
        </p>

        <div className="mt-8 grid gap-3 sm:grid-cols-2">
          <Card
            label="Verified mastery"
            body="Nothing here says &ldquo;complete.&rdquo; A concept only moves on evidence — and the evidence records how much it can be trusted."
          />
          <Card
            label="The one number"
            body="Mastery-per-minute. Reported honestly, including when it is low — because the point was never the app."
            figure="—"
          />
        </div>
      </div>
    </AppShell>
  );
}

function Card({ label, body, figure }: { label: string; body: string; figure?: string }) {
  return (
    <section className="rounded-lg border border-line bg-surface p-4">
      <div className="flex items-baseline justify-between">
        <h2 className="text-[11px] uppercase tracking-[0.16em] text-ink-3">{label}</h2>
        {figure ? <span className="mono text-lg text-accent">{figure}</span> : null}
      </div>
      <p className="mt-2 text-[13.5px] leading-relaxed text-ink-2">{body}</p>
    </section>
  );
}
