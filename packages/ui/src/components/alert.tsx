import type { ReactNode } from "react";

export type AlertVariant = "error" | "notice";

export interface AlertProps {
  children: ReactNode;
  /**
   * `error` unterbricht den Screenreader (`role="alert"`), `notice` wartet
   * (`role="status"`).
   *
   * Der Unterschied ist Verhalten und nicht Farbe: eine Fehlermeldung, die
   * wartet, kommt zu spät; eine Bestätigung, die unterbricht, ist Lärm.
   */
  variant?: AlertVariant;
  className?: string;
}

/**
 * Eine Meldung an genau der Stelle, an der sie entstanden ist.
 *
 * Der Standard ist `error`, weil der Bestand fast überall `role="alert"` setzt.
 * Wäre `notice` der Standard, würde jede vergessene Variante eine
 * Fehlermeldung leise entschärfen.
 */
export function Alert({ children, variant = "error", className }: AlertProps) {
  const classes = ["wt-alert", `wt-alert--${variant}`, className].filter(Boolean).join(" ");

  return (
    <p className={classes} role={variant === "error" ? "alert" : "status"}>
      {children}
    </p>
  );
}
