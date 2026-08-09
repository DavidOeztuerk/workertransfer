export interface LoadingProps {
  /**
   * Was lädt — nicht *dass* etwas lädt.
   *
   * Pflichtfeld, weil die Bestandstexte je Route andere sind („Portfolio wird
   * geladen…", „Freigaben werden geladen…", „Marktstatus wird geladen…"). Ein
   * Standardwert hier würde diese Auskunft einsammeln und wegwerfen.
   */
  label: string;
  className?: string;
}

/**
 * Die Ladeanzeige.
 *
 * `role="status"` und nicht `role="alert"`: ein Ladevorgang unterbricht nicht,
 * er wird berichtet, sobald der Screenreader Luft hat.
 */
export function Loading({ label, className }: LoadingProps) {
  return (
    <p className={["wt-loading", className].filter(Boolean).join(" ")} role="status">
      {label}
    </p>
  );
}
