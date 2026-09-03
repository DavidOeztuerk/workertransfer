import { useCallback, useEffect, useRef, useState } from "react";

/**
 * Was ein Endpunkt mit Seitennummern zurückgibt.
 *
 * Ohne `hasNext`/`hasPrevious`, obwohl der Server sie mitschickt: sie folgen
 * zwingend aus `page` und `totalPages`, und ein Feld, das aus zwei anderen
 * folgt, ist eine zweite Wahrheit über dieselbe Sache. Die Leiste rechnet sie
 * sich aus.
 */
export interface Seite<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

/** Ein Ergebnis: entweder eine Seite oder ein Fehlschlag. */
export type Seitenergebnis<T, F> =
  | { ok: true; seite: Seite<T> }
  | { ok: false; fehler: F };

export interface Blaettern<T, F> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  pending: boolean;
  fehler: F | null;
  gehe: (page: number) => void;
  setzeGroesse: (pageSize: number) => void;
  erneut: () => void;
}

/**
 * Blättern nach Seitennummern.
 *
 * <strong>Die Seite ersetzt, sie hängt nicht an.</strong> Der Vorgänger
 * (`useSeiten`) sammelte Seiten mit „mehr laden" — das passt zu einem Zeiger und
 * nicht zu einer Blätterleiste: wer auf Seite 7 springt, will Seite 7 sehen und
 * nicht die Seiten 1 bis 7 untereinander.
 *
 * <strong>Ein Filterwechsel setzt auf Seite 1 zurück</strong>, und zwar
 * während des Zeichnens statt in einem Effekt. Ein Effekt liefe erst nach dem
 * Festschreiben; der Abruf hätte dann einmal mit dem neuen Filter und der alten
 * Seite gefeuert — und wer auf Seite 7 einen Filter setzt, der nur drei Treffer
 * hat, sähe eine leere Liste und hielte sie für das Ergebnis.
 *
 * <strong>Die vorige Seite bleibt stehen, während die nächste lädt.</strong>
 * Die Liste auf leer zu setzen erzeugt bei jedem Klick ein Springen der ganzen
 * Seitenhöhe; das ist der Unterschied zwischen einem Werkzeug und einem
 * Flackern.
 */
export function useBlaettern<T, F>(
  load: (page: number, pageSize: number, signal: AbortSignal) => Promise<Seitenergebnis<T, F>>,
  key: string,
  vorgabeGroesse = 12,
  aktiv = true
): Blaettern<T, F> {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(vorgabeGroesse);
  const [runde, setRunde] = useState(0);
  const [state, setState] = useState<{
    key: string;
    items: T[];
    totalItems: number;
    totalPages: number;
    pending: boolean;
    fehler: F | null;
  }>({ key, items: [], totalItems: 0, totalPages: 1, pending: aktiv, fehler: null });

  // Siehe oben: WÄHREND des Zeichnens, nicht im Effekt.
  if (state.key !== key) {
    setState({ key, items: [], totalItems: 0, totalPages: 1, pending: aktiv, fehler: null });
    setPage(1);
  }

  const ladenRef = useRef(load);
  ladenRef.current = load;

  useEffect(() => {
    if (!aktiv) return;

    const abort = new AbortController();
    let alive = true;

    setState((previous) => ({ ...previous, pending: true }));

    void ladenRef.current(page, pageSize, abort.signal).then(
      (result) => {
        if (!alive) return;
        setState((previous) =>
          result.ok
            ? {
                ...previous,
                items: result.seite.items,
                totalItems: result.seite.totalItems,
                totalPages: result.seite.totalPages,
                pending: false,
                fehler: null,
              }
            : { ...previous, pending: false, fehler: result.fehler }
        );
      },
      () => {
        // Ein Abbruch ist kein Fehlschlag: er kommt vom Wechsel selbst.
        if (alive && !abort.signal.aborted) {
          setState((previous) => ({ ...previous, pending: false }));
        }
      }
    );

    return () => {
      alive = false;
      abort.abort();
    };
  }, [key, page, pageSize, runde, aktiv]);

  const gehe = useCallback((ziel: number) => setPage(Math.max(1, ziel)), []);

  const setzeGroesse = useCallback((neu: number) => {
    setPageSize(neu);
    // Eine andere Grösse bedeutet eine andere Einteilung. Seite 7 von zwölf ist
    // nicht Seite 7 von achtundvierzig — sie könnte gar nicht existieren.
    setPage(1);
  }, []);

  const erneut = useCallback(() => setRunde((n) => n + 1), []);

  return {
    items: state.items,
    page,
    pageSize,
    totalItems: state.totalItems,
    totalPages: state.totalPages,
    pending: state.pending,
    fehler: state.fehler,
    gehe,
    setzeGroesse,
    erneut,
  };
}
