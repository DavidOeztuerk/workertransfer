import { type DependencyList, useEffect, useRef, useState } from "react";

/*
 * Zwilling von `features/work/lib/useAsync.ts`. Er gehörte nach `src/shared/hooks/` —
 * das ist in diesem Durchgang fremdes Territorium. Beim Zusammenlegen der
 * Ströme: dorthin ziehen, beide Kopien löschen.
 */


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
  load: (signal: AbortSignal) => Promise<T>,
  deps: DependencyList,
  aktiv = true
): Abruf<T> {
  const [data, setData] = useState<T | null>(null);
  const [pending, setPending] = useState(aktiv);
  const [runde, setRunde] = useState(0);

  // Die Funktion wechselt bei jedem Rendern die Identität; stünde sie in den
  // Abhängigkeiten, liefe der Abruf endlos. Der Aufrufer nennt stattdessen die
  // Werte, an denen die Antwort wirklich hängt.
  const ladenRef = useRef(load);
  ladenRef.current = load;

  useEffect(() => {
    if (!aktiv) {
      setPending(false);
      return;
    }
    const abort = new AbortController();
    let alive = true;
    setPending(true);
    void ladenRef.current(abort.signal).then(
      (value) => {
        if (!alive) return;
        setData(value);
        setPending(false);
      },
      () => {
        // Die Clients werfen nicht; kommt hier trotzdem etwas an, ist es der
        // Abbruch selbst. Ein hängender Ladezustand wäre die schlechtere Folge.
        if (alive) setPending(false);
      }
    );
    return () => {
      alive = false;
      abort.abort();
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
  key: string;
  cursor: string | undefined;
  items: T[];
  next: string | null;
  pending: boolean;
  fehler: F | null;
}

export function useSeiten<T, F>(
  load: (cursor: string | undefined, signal: AbortSignal) => Promise<SeitenErgebnis<T, F>>,
  key: string,
  aktiv = true
): Blaettern<T, F> {
  const empty = (forKey: string): Lage<T, F> => ({
    key: forKey,
    cursor: undefined,
    items: [],
    next: null,
    pending: aktiv,
    fehler: null,
  });

  const [state, setLage] = useState<Lage<T, F>>(() => empty(key));

  // Zurücksetzen WÄHREND des Renderns, nicht in einem Effekt. Ein Effekt liefe
  // erst nach dem Festschreiben — der Abruf-Effekt hätte dann schon einmal mit
  // dem alten Cursor und dem neuen Schlüssel gefeuert, und zwei Anfragen
  // lieferten sich ein Rennen um dieselbe Liste. React verwirft das laufende
  // Rendern und beginnt neu; ein zusätzlicher Durchlauf, keine zusätzliche
  // Anfrage.
  if (state.key !== key) setLage(empty(key));

  const ladenRef = useRef(load);
  ladenRef.current = load;

  // Der Schlüssel, den der ZUSTAND trägt — nicht der verlangte. Während
  // eines Wechsels sind das zwei verschiedene, und genau darauf beruht
  // die Prüfung weiter unten.
  const stateKey = state.key;
  const cursor = state.cursor;

  useEffect(() => {
    if (!aktiv) return;
    const abort = new AbortController();
    let alive = true;
    void ladenRef.current(cursor, abort.signal).then(
      (result) => {
        if (!alive) return;
        setLage((previous) => {
          if (previous.key !== stateKey || previous.cursor !== cursor) return previous;
          if (!result.ok) return { ...previous, pending: false, fehler: result.fehler };
          return {
            ...previous,
            pending: false,
            fehler: null,
            // Ohne Cursor ist es eine neue Suche; mit Cursor kommt sie dazu.
            items: cursor === undefined ? result.items : [...previous.items, ...result.items],
            next: result.nextCursor,
          };
        });
      },
      () => {
        if (alive) setLage((previous) => ({ ...previous, pending: false }));
      }
    );
    return () => {
      alive = false;
      abort.abort();
    };
  }, [stateKey, cursor, aktiv]);

  return {
    items: state.items,
    pending: state.pending,
    fehler: state.fehler,
    mehr: state.next !== null,
    weiter: () =>
      setLage((previous) =>
        previous.next === null ? previous : { ...previous, cursor: previous.next, pending: true }
      ),
  };
}
