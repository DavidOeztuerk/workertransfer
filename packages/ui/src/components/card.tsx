import type { HTMLAttributes, ReactNode } from "react";

export type CardProps = HTMLAttributes<HTMLElement> & {
  children: ReactNode;
};

export function Card({ children, className, ...props }: CardProps) {
  const classes = ["wt-card", className].filter(Boolean).join(" ");

  return (
    <section className={classes} {...props}>
      {children}
    </section>
  );
}
