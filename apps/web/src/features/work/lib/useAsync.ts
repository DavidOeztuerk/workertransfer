import { type DependencyList, useEffect, useRef, useState } from "react";

/**
 * Ein Abruf, der zur Seite gehört — mehr braucht es hier nicht.
 *
 * <strong>Warum kein TanStack Query.</strong> Es liegt zwar im Paketbaum, aber
 * es gibt keinen `QueryClientProvider`: `AppRoot.tsx` ist `ThemeProvider` +
 * `RouterProvider`, sonst nichts. Ein `useQuery` würde in der echten Anwendung
 * werfen („No QueryClient set") — und der Testhelfer stellt einen bereit, also
 * wäre der Test grün, während die Seite weiß bleibt. Das ist die schlechteste
 * Sorte Fehler: einer, den die Prüfung deckt.
 *
 * <strong>Warum kein Slice.</strong> `core/store/store.ts` meldet `auth` und
 * `preferences` an und liegt außerhalb jedes Feature-Territoriums. Ein
 * `workSlice` wäre nirgends registriert, und `useAppSelector(s => s.work)` käme
 * als `undefined` zurück.
 *
 * Bleibt: `useState` + `useEffect` + `AbortController`. Der Abbruch ist kein
 * Beiwerk — wer während eines laufenden Abrufs weiterklickt, bekommt sonst die
 * Antwort der verlassenen Seite in die neue geschrieben.
 */
export interface Abruf<T> {
  /** Läuft der erste oder ein erneuter Abruf? */
  pending: boolean;
  /** Die letzte Antwort, oder `null`, solange keine da ist. */
  data: T | null;
  /** Noch einmal fragen — nach einer Änderung, die die Antwort verschiebt. */
  reload: () => void;
}

export function useAsync<T>(
  laden: (signal: AbortSignal) => Promise<T>,
  deps: DependencyList,
  aktiv = true
): Abruf<T> {
  const [data, setData] = useState<T | null>(null);
  const [pending, setPending] = useState(aktiv);
  const [runde, setRunde] = useState(0);

  // Die Funktion wechselt bei jedem Rendern die Identität; stünde sie in den
  // Abhängigkeiten, liefe der Abruf endlos. Der Aufrufer nennt stattdessen die
  // Werte, an denen die Antwort wirklich hängt.
  const ladenRef = useRef(laden);
  ladenRef.current = laden;

  useEffect(() => {
    if (!aktiv) {
      setPending(false);
      return;
    }
    const abbruch = new AbortController();
    let lebt = true;
    setPending(true);
    void ladenRef.current(abbruch.signal).then(
      (wert) => {
        if (!lebt) return;
        setData(wert);
        setPending(false);
      },
      () => {
        // Die Clients werfen nicht; kommt hier trotzdem etwas an, ist es der
        // Abbruch selbst. Ein hängender Ladezustand wäre die schlechtere Folge.
        if (lebt) setPending(false);
      }
    );
    return () => {
      lebt = false;
      abbruch.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, runde, aktiv]);

  return { pending, data, reload: () => setRunde((n) => n + 1) };
}

/**
 * Eine Liste, die am Cursor weiterblättert — und die vorigen Seiten behält.
 *
 * Das Behalten ist die eigentliche Zusage: wer „Mehr laden" drückt, will
 * dazubekommen, nicht ersetzt bekommen. Scheitert eine spätere Seite, bleiben
 * die früheren stehen — sie waren echt.
 *
 * `schluessel` ist die Suche in einer Zeichenkette. Ändert er sich, beginnt die
 * Liste von vorn: neuer Cursor, leere Sammlung, neuer Ladezustand.
 */
export interface Seite<T> {
  items: T[];
  nextCursor: string | null;
}

export type SeitenErgebnis<T, F> = ({ ok: true } & Seite<T>) | { ok: false; fehler: F };

export interface Blaettern<T, F> {
  items: T[];
  pending: boolean;
  fehler: F | null;
  /** Gibt es eine weitere Seite? */
  mehr: boolean;
  weiter: () => void;
}

interface Lage<T, F> {
  schluessel: string;
  cursor: string | undefined;
  items: T[];
  next: string | null;
  pending: boolean;
  fehler: F | null;
}

export function useSeiten<T, F>(
  laden: (cursor: string | undefined, signal: AbortSignal) => Promise<SeitenErgebnis<T, F>>,
  schluessel: string,
  aktiv = true
): Blaettern<T, F> {
  const leer = (key: string): Lage<T, F> => ({
    schluessel: key,
    cursor: undefined,
    items: [],
    next: null,
    pending: aktiv,
    fehler: null,
  });

  const [lage, setLage] = useState<Lage<T, F>>(() => leer(schluessel));

  // Zurücksetzen WÄHREND des Renderns, nicht in einem Effekt. Ein Effekt liefe
  // erst nach dem Festschreiben — der Abruf-Effekt hätte dann schon einmal mit
  // dem alten Cursor und dem neuen Schlüssel gefeuert, und zwei Anfragen
  // lieferten sich ein Rennen um dieselbe Liste. React verwirft das laufende
  // Rendern und beginnt neu; ein zusätzlicher Durchlauf, keine zusätzliche
  // Anfrage.
  if (lage.schluessel !== schluessel) setLage(leer(schluessel));

  const ladenRef = useRef(laden);
  ladenRef.current = laden;

  const key = lage.schluessel;
  const cursor = lage.cursor;

  useEffect(() => {
    if (!aktiv) return;
    const abbruch = new AbortController();
    let lebt = true;
    void ladenRef.current(cursor, abbruch.signal).then(
      (ergebnis) => {
        if (!lebt) return;
        setLage((bisher) => {
          if (bisher.schluessel !== key || bisher.cursor !== cursor) return bisher;
          if (!ergebnis.ok) return { ...bisher, pending: false, fehler: ergebnis.fehler };
          return {
            ...bisher,
            pending: false,
            fehler: null,
            // Ohne Cursor ist es eine neue Suche; mit Cursor kommt sie dazu.
            items: cursor === undefined ? ergebnis.items : [...bisher.items, ...ergebnis.items],
            next: ergebnis.nextCursor,
          };
        });
      },
      () => {
        if (lebt) setLage((bisher) => ({ ...bisher, pending: false }));
      }
    );
    return () => {
      lebt = false;
      abbruch.abort();
    };
  }, [key, cursor, aktiv]);

  return {
    items: lage.items,
    pending: lage.pending,
    fehler: lage.fehler,
    mehr: lage.next !== null,
    weiter: () =>
      setLage((bisher) =>
        bisher.next === null ? bisher : { ...bisher, cursor: bisher.next, pending: true }
      ),
  };
}
