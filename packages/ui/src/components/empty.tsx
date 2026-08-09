import type { ReactNode } from "react";

export interface EmptyProps {
  /** Was nicht da ist — in einem Satz, aus Sicht der Person. */
  title: string;
  /** Warum das in Ordnung ist, oder was es bedeutet. */
  hint?: ReactNode;
  /** Der Weg heraus, falls es einen gibt. */
  action?: ReactNode;
  className?: string;
}

/**
 * „Hier ist noch nichts."
 *
 * Bewusst **ohne** `role`: ein Leerzustand ist kein Fehler und kein
 * Ladevorgang. Mit `role="status"` würde jede leere Liste beim Aufbau der
 * Seite vorgelesen.
 */
export function Empty({ title, hint, action, className }: EmptyProps) {
  return (
    <div className={["wt-empty", className].filter(Boolean).join(" ")}>
      <p className="wt-empty__title">{title}</p>
      {hint !== undefined ? <p className="wt-empty__hint">{hint}</p> : null}
      {action !== undefined ? <div className="wt-empty__action">{action}</div> : null}
    </div>
  );
}
