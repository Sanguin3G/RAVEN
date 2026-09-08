import type { HTMLAttributes, ReactNode } from "react";

interface PanelProps extends HTMLAttributes<HTMLElement> {
  children: ReactNode;
  title?: string;
  eyebrow?: string;
}

export function Panel({ children, className = "", eyebrow, title, ...props }: PanelProps) {
  return (
    <section className={`panel ${className}`.trim()} {...props}>
      {eyebrow || title ? (
        <header className="panel__heading">
          <div>
            {eyebrow ? <p className="eyebrow">{eyebrow}</p> : null}
            {title ? <h2>{title}</h2> : null}
          </div>
        </header>
      ) : null}
      {children}
    </section>
  );
}
