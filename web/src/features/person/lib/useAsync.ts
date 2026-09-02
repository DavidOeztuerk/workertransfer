import { useCallback, useEffect, useState } from "react";

/**
 * Laden ohne TanStack Query.
 *
 * <strong>Gemessen, nicht gewählt.</strong> `AppRoot.tsx` hängt keinen
 * `QueryClientProvider` ein — `useQuery` aus dem alten Code würde in der echten
 * Anwendung werfen („No QueryClient set"). Der ALTE Testhelfer stellte einen
 * bereit, ein Test wäre also grün gewesen, während die Seite weiss bleibt.
 *
 * Ein `personSlice` scheidet ebenso aus: `core/store/store.ts` meldet `auth` und
 * `preferences` an und liegt ausserhalb dieses Territoriums.
 *
 * Der Haken kennt drei Zustände, und das ist der Punkt: `laedt` ist nicht
 * „leer", und `wert === undefined` ist nicht „nichts vorhanden".
 */
export interface AsyncZustand<T> {
  wert: T | undefined;
  laedt: boolean;
  /** Neu laden — nach einer Änderung, die die Antwort verändert hat. */
  erneut: () => void;
  /** Den Wert von Hand setzen, wenn die Antwort ihn schon mitbrachte. */
  setze: (wert: T) => void;
}

/**
 * @param laden ruft den Dienst. Bekommt ein `AbortSignal`.
 * @param aktiv `false` heisst „gar nicht erst fragen" (etwa: niemand angemeldet).
 *   Der Zustand bleibt dann für immer im Laden — die Seite fragt vorher ab.
 */
export function useAsync<T>(
  laden: (signal: AbortSignal) => Promise<T>,
  deps: unknown[],
  aktiv = true
): AsyncZustand<T> {
  const [wert, setzeWert] = useState<T | undefined>(undefined);
  const [laedt, setzeLaedt] = useState(aktiv);
  const [runde, setzeRunde] = useState(0);

  // Die Abhängigkeiten kommen vom Aufrufer; `laden` selbst ist bei jedem
  // Rendern eine neue Funktion und darf deshalb NICHT in die Liste.
  const ruf = useCallback(laden, deps);

  useEffect(() => {
    if (!aktiv) {
      setzeLaedt(false);
      return;
    }
    const abbruch = new AbortController();
    let lebt = true;
    setzeLaedt(true);
    void ruf(abbruch.signal).then(
      (antwort) => {
        if (!lebt) return;
        setzeWert(antwort);
        setzeLaedt(false);
      },
      () => {
        // Ein abgebrochener Aufruf ist kein Ergebnis. Die API-Module werfen
        // ohnehin nicht — ein Statuscode ist dort eine Antwort.
        if (lebt) setzeLaedt(false);
      }
    );
    return () => {
      lebt = false;
      abbruch.abort();
    };
  }, [ruf, aktiv, runde]);

  return {
    wert,
    laedt,
    erneut: useCallback(() => setzeRunde((n) => n + 1), []),
    setze: useCallback((neu: T) => {
      setzeWert(neu);
      setzeLaedt(false);
    }, []),
  };
}
