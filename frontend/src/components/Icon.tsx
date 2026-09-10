import type { SVGProps } from "react";

export type IconName = "buildings" | "chevron-left" | "chevron-right" | "dashboard" | "gear" | "magnifying-glass" | "menu" | "x";

type IconProps = SVGProps<SVGSVGElement> & {
  name: IconName;
  size?: number;
};

/**
 * Small, dependency-free icon set for the application shell.
 *
 * These icons are decorative in navigation controls; the containing button or
 * link supplies the accessible name.
 */
export function Icon({ name, size = 18, ...props }: IconProps) {
  return (
    <svg
      aria-hidden="true"
      focusable="false"
      height={size}
      viewBox="0 0 24 24"
      width={size}
      {...props}
    >
      {name === "dashboard" && (
        <>
          <rect height="8" rx="1" stroke="currentColor" strokeWidth="1.8" width="8" x="3" y="3" />
          <rect height="8" rx="1" stroke="currentColor" strokeWidth="1.8" width="8" x="13" y="3" />
          <rect height="8" rx="1" stroke="currentColor" strokeWidth="1.8" width="8" x="3" y="13" />
          <rect height="8" rx="1" stroke="currentColor" strokeWidth="1.8" width="8" x="13" y="13" />
        </>
      )}
      {name === "buildings" && (
        <>
          <path d="M4 21V5a1 1 0 0 1 1-1h9a1 1 0 0 1 1 1v16M15 9h4a1 1 0 0 1 1 1v11M2 21h20" fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8" />
          <path d="M8 8h2M8 12h2M8 16h2M17 13h1M17 17h1" fill="none" stroke="currentColor" strokeLinecap="round" strokeWidth="1.8" />
        </>
      )}
      {name === "magnifying-glass" && (
        <path d="m15.5 15.5 5 5M10.75 18.25a7.5 7.5 0 1 0 0-15 7.5 7.5 0 0 0 0 15Z" fill="none" stroke="currentColor" strokeLinecap="round" strokeWidth="1.8" />
      )}
      {name === "gear" && (
        <>
          <path d="M12 8.25a3.75 3.75 0 1 0 0 7.5 3.75 3.75 0 0 0 0-7.5Z" fill="none" stroke="currentColor" strokeWidth="1.8" />
          <path d="m19.4 15 .1.06a1.5 1.5 0 0 1-1.5 2.6l-.1-.06a1.5 1.5 0 0 0-2.25 1.3V19a1.5 1.5 0 0 1-3 0v-.12a1.5 1.5 0 0 0-2.25-1.3l-.1.06a1.5 1.5 0 1 1-1.5-2.6l.1-.06a1.5 1.5 0 0 0 0-2.6l-.1-.06a1.5 1.5 0 1 1 1.5-2.6l.1.06a1.5 1.5 0 0 0 2.25-1.3V8a1.5 1.5 0 0 1 3 0v.12a1.5 1.5 0 0 0 2.25 1.3l.1-.06a1.5 1.5 0 1 1 1.5 2.6l-.1.06a1.5 1.5 0 0 0 0 2.6Z" fill="none" stroke="currentColor" strokeLinejoin="round" strokeWidth="1.8" />
        </>
      )}
      {name === "chevron-left" && <path d="m14.5 5-7 7 7 7" fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" />}
      {name === "chevron-right" && <path d="m9.5 5 7 7-7 7" fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" />}
      {name === "menu" && <path d="M4 6h16M4 12h16M4 18h16" fill="none" stroke="currentColor" strokeLinecap="round" strokeWidth="2" />}
      {name === "x" && <path d="m6 6 12 12M18 6 6 18" fill="none" stroke="currentColor" strokeLinecap="round" strokeWidth="2" />}
    </svg>
  );
}
