// Die Auskunft: zusammengesetzt im Browser aus den Diensten, die es schon gibt.
//
// Ein Dienst, der alles einsammelt, müsste über sieben Dienstgrenzen hinweg
// lesen — genau das, was ADR-0004 ausschließt. Und es sind ausnahmslos
// Endpunkte, die ohnehin nur die eigenen Daten herausgeben: der Export erfindet
// keinen neuen Zugriff, er bündelt vorhandene.

export type SectionState = "ok" | "nicht_abrufbar";

export interface Section {
  status: SectionState;
  daten?: unknown;
}

export interface DataExport {
  erzeugt_am: string;
  /** Welche Abschnitte fehlen. Leer heißt: nichts fehlt. */
  unvollständig: string[];
  abschnitte: Record<string, Section>;
}

/**
 * Ein Abschnitt aus einem Ergebnis, das gelingen oder scheitern kann.
 *
 * Der springende Punkt: ein gescheiterter Abschnitt wird NICHT weggelassen. Er
 * bleibt in der Datei stehen und sagt, dass er fehlt. Ihn stillschweigend
 * auszulassen wäre hier der schlimmste Fehler — die Datei sähe vollständig aus,
 * und jemand würde daraus schließen, es gebe nichts weiter über ihn.
 */
export function section(ok: boolean, daten: unknown): Section {
  return ok ? { status: "ok", daten } : { status: "nicht_abrufbar" };
}

export function buildExport(
  abschnitte: Record<string, Section>,
  now: Date = new Date()
): DataExport {
  const missing = Object.entries(abschnitte)
    .filter(([, value]) => value.status !== "ok")
    .map(([name]) => name);
  return {
    erzeugt_am: now.toISOString(),
    unvollständig: missing,
    abschnitte,
  };
}

/** Ein Dateiname, der in einem Downloads-Ordner noch etwas sagt. */
export function exportFilename(now: Date = new Date()): string {
  return `workertransfer-meine-daten-${now.toISOString().slice(0, 10)}.json`;
}
