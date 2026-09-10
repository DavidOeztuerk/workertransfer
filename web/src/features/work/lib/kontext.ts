/**
 * Briefdaten, die ein Seitenwechsel nicht noch einmal holt.
 *
 * Anschrift, Lebenslauf und Firmenprofil ändern sich nicht, während das
 * Modell schreibt. Sie bei jedem Mount neu zu fragen erzeugt die 404er
 * in der Konsole (`/resumes/me`, `/companies/…/profile`) und bricht
 * laufende Abrufe ab (499).
 *
 * ## Verwerfen muss den laufenden Abruf mitnehmen
 *
 * Bis zum 10.09.2026 leerten `vergiss` und `vergissKontext` nur die Maps —
 * ein Abruf, der schon unterwegs war, lief weiter und schrieb sein Ergebnis
 * **danach** hinein. Wer also seinen Lebenslauf ändert, während der alte
 * Abruf noch läuft, bekam den alten Stand zurück in den Zwischenspeicher, und
 * die Seite zeigte ihn bis zum nächsten harten Neuladen.
 *
 * Gefunden hat es die Testreihe, und zwar auf dem Läufer statt hier:
 * `DraftPage` fiel dort an „Lebenslauf bearbeiten", weil eine Antwort aus dem
 * vorigen Fall nach dem `afterEach` ankam und den Zwischenspeicher wieder
 * füllte. Lokal war der Abruf immer vorher fertig — fünf Läufe grün. Eine
 * höhere Wartegrenze hat daran nichts geändert und konnte es auch nicht: es
 * war nie zu langsam, es war der falsche Wert.
 *
 * Der Stand ist deshalb **je Schlüssel** gezählt. Ein Abruf schreibt nur,
 * wenn seit seinem Beginn niemand denselben Schlüssel verworfen hat. Ein
 * globaler Zähler wäre kürzer und schlechter: er würfe bei jedem `vergiss`
 * auch die Abrufe aller anderen Schlüssel weg.
 */

const werte = new Map<string, unknown>();
const laufend = new Map<string, Promise<unknown>>();

/**
 * Wie oft dieser Schlüssel verworfen wurde.
 *
 * Wird bewusst NIE geleert, auch von `vergissKontext` nicht: ein Abruf, der
 * noch unterwegs ist, muss seinen Stand nach dem Leeren wiedererkennen können.
 * Die Menge der Schlüssel ist klein und fest (Lebenslauf, Anschrift, Stelle,
 * Firma, Profil), es wächst also nichts.
 */
const stand = new Map<string, number>();

function standVon(schluessel: string): number {
  return stand.get(schluessel) ?? 0;
}

function verwirf(schluessel: string): void {
  stand.set(schluessel, standVon(schluessel) + 1);
  werte.delete(schluessel);
  laufend.delete(schluessel);
}

export function merke<T>(
  schluessel: string,
  laden: () => Promise<T>,
): Promise<T> {
  if (werte.has(schluessel)) {
    return Promise.resolve(werte.get(schluessel) as T);
  }

  const schon = laufend.get(schluessel) as Promise<T> | undefined;
  if (schon) return schon;

  const meiner = standVon(schluessel);

  const arbeit = laden().then(
    (wert) => {
      // NUR SCHREIBEN, WENN NIEMAND DAZWISCHEN VERWORFEN HAT. Ohne diese
      // Prüfung macht ein spät ankommender Abruf jedes `vergiss` rückgängig.
      if (standVon(schluessel) === meiner) {
        werte.set(schluessel, wert);
        laufend.delete(schluessel);
      }
      return wert;
    },
    (fehler: unknown) => {
      if (standVon(schluessel) === meiner) {
        laufend.delete(schluessel);
      }
      throw fehler;
    },
  );

  laufend.set(schluessel, arbeit);
  return arbeit;
}

/** Eine Antwort verwerfen, die sich auf der Seite geändert hat. */
export function vergiss(schluessel: string): void {
  verwirf(schluessel);
}

/** Nur für Tests: sonst hängt ein 404 aus dem vorigen Fall. */
export function vergissKontext(): void {
  for (const schluessel of new Set([...werte.keys(), ...laufend.keys()])) {
    verwirf(schluessel);
  }
  werte.clear();
  laufend.clear();
}
