/**
 * Die Datenauskunft — als Datei, und ausschliesslich im Browser gebaut.
 *
 * <strong>Sie wird nirgends abgelegt.</strong> Es gibt keinen Endpunkt, der sie
 * erzeugt, und keine Datei, die auf einem Server liegen bleibt — sonst hätte
 * eine Auskunft über die eigenen Daten selbst wieder Daten hinterlassen.
 */
export type Abschnittsstand = "ok" | "nicht_abrufbar";

export interface Abschnitt {
  status: Abschnittsstand;
  daten?: unknown;
}

export interface Datenauskunft {
  erzeugt_am: string;
  /** Welche Abschnitte fehlen. Leer heisst: nichts fehlt. */
  unvollständig: string[];
  abschnitte: Record<string, Abschnitt>;
}

/**
 * Ein Abschnitt, und ob er wirklich da ist.
 *
 * <strong>Fehlt er, stehen auch keine Daten darin.</strong> Ein Abschnitt mit
 * `nicht_abrufbar` und trotzdem gefülltem Feld wäre die schlimmste Variante:
 * halb wahr, und niemand sieht, welche Hälfte.
 */
export const abschnitt = (ok: boolean, daten: unknown): Abschnitt =>
  ok ? { status: "ok", daten } : { status: "nicht_abrufbar" };

/**
 * Baut die Auskunft und sagt oben, was fehlt.
 *
 * Die Liste der Lücken steht IN der Datei, nicht nur auf der Seite: wer sie
 * später öffnet, soll ohne die Seite erkennen, dass sie unvollständig ist.
 */
export function baueAuskunft(
  abschnitte: Record<string, Abschnitt>,
  jetzt: Date = new Date()
): Datenauskunft {
  return {
    erzeugt_am: jetzt.toISOString(),
    unvollständig: Object.entries(abschnitte)
      .filter(([, wert]) => wert.status !== "ok")
      .map(([name]) => name),
    abschnitte,
  };
}

/** Ein Dateiname, der in einem Downloads-Ordner noch etwas sagt. */
export const dateiname = (jetzt: Date = new Date()): string =>
  `workertransfer-meine-daten-${jetzt.toISOString().slice(0, 10)}.json`;
