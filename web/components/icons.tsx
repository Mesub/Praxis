/*
  A small, consistent icon set — inline SVG, single 1.5px stroke, 24px box (never emoji,
  which are font-dependent and cannot be themed). One visual language across the console.
*/
import type { SVGProps } from "react";

function Icon({ children, ...props }: SVGProps<SVGSVGElement>) {
  return (
    <svg
      width={18}
      height={18}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.5}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      {...props}
    >
      {children}
    </svg>
  );
}

export const TodayIcon = (p: SVGProps<SVGSVGElement>) => (
  <Icon {...p}>
    <rect x="3" y="4" width="18" height="18" rx="2" />
    <path d="M3 10h18M8 2v4M16 2v4" />
  </Icon>
);

export const GraphIcon = (p: SVGProps<SVGSVGElement>) => (
  <Icon {...p}>
    <circle cx="6" cy="6" r="2.5" />
    <circle cx="18" cy="7" r="2.5" />
    <circle cx="12" cy="18" r="2.5" />
    <path d="M8 7.5 15.5 8M7 8l4 8M16.5 9l-4 7" />
  </Icon>
);

export const RepsIcon = (p: SVGProps<SVGSVGElement>) => (
  <Icon {...p}>
    <path d="M21 12a9 9 0 1 1-2.64-6.36" />
    <path d="M21 4v5h-5" />
  </Icon>
);

export const LoopsIcon = (p: SVGProps<SVGSVGElement>) => (
  <Icon {...p}>
    <path d="M4 12a8 8 0 0 1 8-8M20 12a8 8 0 0 1-8 8" />
    <path d="M12 4 9 7M12 20l3-3" />
  </Icon>
);

export const LedgerIcon = (p: SVGProps<SVGSVGElement>) => (
  <Icon {...p}>
    <path d="M6 3h9l3 3v15H6z" />
    <path d="M9 8h6M9 12h6M9 16h4" />
  </Icon>
);

export const AnalyticsIcon = (p: SVGProps<SVGSVGElement>) => (
  <Icon {...p}>
    <path d="M4 20V4M4 20h16" />
    <path d="M8 16v-4M13 16V8M18 16v-7" />
  </Icon>
);
