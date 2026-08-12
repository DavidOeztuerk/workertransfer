import type { ReactNode } from "react";

export interface VisuallyHiddenProps {
  children: ReactNode;
}

/**
 * Text, der gelesen, aber nicht gesehen werden soll.
 *
 * Das **Gegenteil** von `aria-hidden`, und der Unterschied ist der ganze
 * Zweck: der Text bleibt für den Vorleser da und verschwindet nur für das Auge
 * — die Wörter hinter ✓ und ✗, ohne die man drei Namen und keinen Unterschied
 * hört.
 */
export function VisuallyHidden({ children }: VisuallyHiddenProps) {
  return <span className="wt-visually-hidden">{children}</span>;
}
