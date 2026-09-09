/**
 * Briefdaten, die ein Seitenwechsel nicht noch einmal holt.
 *
 * Anschrift, Lebenslauf und Firmenprofil ändern sich nicht, während das
 * Modell schreibt. Sie bei jedem Mount neu zu fragen erzeugt die 404er
 * in der Konsole (`/resumes/me`, `/companies/…/profile`) und bricht
 * laufende Abrufe ab (499).
 */

const werte = new Map<string, unknown>();
const laufend = new Map<string, Promise<unknown>>();

export function merke<T>(
  schluessel: string,
  laden: () => Promise<T>,
): Promise<T> {
  if (werte.has(schluessel)) {
    return Promise.resolve(werte.get(schluessel) as T);
  }

  const schon = laufend.get(schluessel) as Promise<T> | undefined;
  if (schon) return schon;

  const arbeit = laden().then(
    (wert) => {
      werte.set(schluessel, wert);
      laufend.delete(schluessel);
      return wert;
    },
    (fehler: unknown) => {
      laufend.delete(schluessel);
      throw fehler;
    },
  );
  laufend.set(schluessel, arbeit);
  return arbeit;
}

/** Eine Antwort verwerfen, die sich auf der Seite geändert hat. */
export function vergiss(schluessel: string): void {
  werte.delete(schluessel);
  laufend.delete(schluessel);
}

/** Nur für Tests: sonst hängt ein 404 aus dem vorigen Fall. */
export function vergissKontext(): void {
  werte.clear();
  laufend.clear();
}
