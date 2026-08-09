import { useEffect } from "react";

export interface ToastProps {
  message: string;
  onDismiss: () => void;
  /**
   * Nach dieser Zeit verschwindet die Meldung von selbst.
   *
   * **Ohne Angabe bleibt sie stehen**, und das ist der Standard mit Absicht:
   * eine Meldung, die von selbst geht, ist schlechter als eine, die bleibt,
   * sobald jemand danach handeln muss. Eine Zeitgrenze ist eine Zusage, die man
   * gibt — nicht eine, die man erbt.
   */
  durationMs?: number;
  dismissLabel?: string;
  className?: string;
}

/**
 * Eine kurze Bestätigung — „Gespeichert.", „Freigabe erteilt."
 *
 * Nur für Dinge, die **erledigt** sind. Nicht für Fehler (die gehören als
 * `Alert` an die Stelle, an der sie entstanden), nicht für etwas, das noch
 * läuft, und **niemals** für die Zusage aus ADR-0027 §6 („Deine Löschung ist
 * angenommen und läuft") — die muss auf der Seite stehen bleiben, wo die Person
 * sie wiederfindet.
 *
 * Die aria-Attribute trägt dieses Bauteil selbst und benutzt nicht
 * `LiveRegion`: die ist absichtlich unsichtbar, ein Toast ist sichtbar. Beide
 * sagen dasselbe an, nur einer davon zeigt es auch.
 */
export function Toast({
  message,
  onDismiss,
  durationMs,
  dismissLabel = "Ausblenden",
  className,
}: ToastProps) {
  useEffect(() => {
    if (durationMs === undefined) return;
    const timer = setTimeout(onDismiss, durationMs);
    return () => clearTimeout(timer);
  }, [durationMs, onDismiss]);

  return (
    <div className={["wt-toast", className].filter(Boolean).join(" ")}>
      <p className="wt-toast__message" role="status" aria-live="polite">
        {message}
      </p>
      {/* Von Hand ausblenden geht immer — auch wenn eine Zeitgrenze gesetzt
          ist. Wer gelesen hat, muss nicht warten. */}
      <button type="button" className="wt-button wt-button--quiet" onClick={onDismiss}>
        {dismissLabel}
      </button>
    </div>
  );
}
