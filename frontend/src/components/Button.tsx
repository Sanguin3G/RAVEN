import type { ButtonHTMLAttributes, ReactNode } from "react";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  children: ReactNode;
  tone?: "primary" | "secondary" | "quiet";
  loading?: boolean;
}

export function Button({ children, className = "", disabled, loading = false, tone = "primary", ...props }: ButtonProps) {
  return (
    <button
      className={`button button--${tone} ${className}`.trim()}
      disabled={disabled || loading}
      {...props}
    >
      {loading ? "Working…" : children}
    </button>
  );
}
