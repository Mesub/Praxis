import Link from "next/link";
import type { ReactNode } from "react";
import {
  AnalyticsIcon,
  GraphIcon,
  LedgerIcon,
  LoopsIcon,
  RepsIcon,
  TodayIcon,
} from "./icons";

/*
  The console frame from the prototype: a fixed 248px rail and a topbar, with the page in
  the main column. Pages (M11.2+) render inside `AppShell`; this file owns nothing but the
  chrome, so the navigation sits in one place and stays identical on every page.
*/

type NavItem = {
  href: string;
  label: string;
  icon: (p: { className?: string }) => ReactNode;
  badge?: string;
};

const PRIMARY: NavItem[] = [
  { href: "/", label: "Today", icon: TodayIcon },
  { href: "/graph", label: "The Graph", icon: GraphIcon },
  { href: "/reps", label: "Reps", icon: RepsIcon, badge: "0" },
  { href: "/loops", label: "Loops", icon: LoopsIcon },
];

const RECORD: NavItem[] = [
  { href: "/ledger", label: "The Ledger", icon: LedgerIcon },
  { href: "/analytics", label: "Analytics", icon: AnalyticsIcon },
];

export function AppShell({
  crumb,
  current,
  children,
}: {
  crumb: string;
  current: string;
  children: ReactNode;
}) {
  return (
    <div className="grid min-h-dvh grid-cols-1 md:grid-cols-[var(--rail)_1fr]">
      <Rail current={current} />
      <div className="flex min-w-0 flex-col">
        <header className="flex items-center justify-between border-b border-line px-4 py-3.5 md:px-8">
          <span className="text-[11px] uppercase tracking-[0.16em] text-ink-3">{crumb}</span>
          <span className="inline-flex items-center gap-2 rounded-full border border-line bg-surface px-2.5 py-1 text-[11.5px] text-ink-2">
            <span className="h-1.5 w-1.5 rounded-full bg-accent" aria-hidden="true" />
            Phase 0
          </span>
        </header>
        <main className="min-w-0 flex-1 px-4 py-6 md:px-8 md:py-8">{children}</main>
      </div>
    </div>
  );
}

function Rail({ current }: { current: string }) {
  return (
    <nav
      aria-label="Primary"
      className="hidden flex-col border-r border-line bg-bg px-3.5 py-4 md:flex"
    >
      <div className="px-2 pb-4">
        <div className="serif text-[19px] font-semibold tracking-[0.14em] text-ink">PRAXIS</div>
        <div className="-mt-0.5 text-[10px] uppercase tracking-[0.16em] text-ink-3">
          Mastery OS
        </div>
      </div>

      <NavGroup title="Practice" items={PRIMARY} current={current} />
      <NavGroup title="Record" items={RECORD} current={current} />

      <p className="mt-auto border-t border-line px-3 pt-3.5 text-[10.5px] leading-relaxed text-ink-4">
        Mastery only moves via evidence. There is no &ldquo;mark as complete.&rdquo;
      </p>
    </nav>
  );
}

function NavGroup({
  title,
  items,
  current,
}: {
  title: string;
  items: NavItem[];
  current: string;
}) {
  return (
    <div className="mb-4">
      <h2 className="mb-1.5 px-3 text-[10px] font-semibold uppercase tracking-[0.18em] text-ink-4">
        {title}
      </h2>
      <ul className="flex flex-col gap-0.5">
        {items.map((item) => {
          const active = item.label === current;
          const Glyph = item.icon;
          return (
            <li key={item.href}>
              <Link
                href={item.href}
                aria-current={active ? "page" : undefined}
                // ≥44px touch target; the active item takes the surface-2 fill and brass
                // glyph from the prototype. Transitions ride the shared 220ms/brand easing.
                className={`flex min-h-[44px] items-center gap-3 rounded-md px-3 text-[13.5px] transition-[background-color,color] duration-[220ms] ease-brand ${
                  active
                    ? "bg-surface-2 font-medium text-ink"
                    : "text-ink-2 hover:bg-surface hover:text-ink"
                }`}
              >
                <Glyph className={active ? "text-accent" : "text-ink-3"} />
                <span>{item.label}</span>
                {item.badge ? (
                  <span className="mono ml-auto text-[10px] text-ink-4">{item.badge}</span>
                ) : null}
              </Link>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
