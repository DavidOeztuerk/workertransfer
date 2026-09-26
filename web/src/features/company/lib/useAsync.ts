import { type DependencyList, useEffect, useRef, useState } from "react";

/*
 * Twin of `features/work/lib/useAsync.ts`. It belonged in `src/shared/hooks/` —
 * that was foreign territory in this pass. When the streams are merged: move it
 * there, delete both copies.
 */


/**
 * A fetch that belongs to the page — nothing more is needed here.
 *
 * <strong>Why no TanStack Query.</strong> It is in the package tree, but there
 * is no `QueryClientProvider`: `AppRoot.tsx` is `ThemeProvider` +
 * `RouterProvider`, nothing else. A `useQuery` would throw in the real
 * application ("No QueryClient set") — and the test helper provides one, so the
 * test would be green while the page stays white. That is the worst kind of
 * defect: one the check covers up.
 *
 * <strong>Why no slice.</strong> `core/store/store.ts` registers `auth` and
 * `preferences` and lies outside every feature's territory. A `workSlice` would
 * be registered nowhere, and `useAppSelector(s => s.work)` would come back as
 * `undefined`.
 *
 * What remains: `useState` + `useEffect` + `AbortController`. The abort is no
 * accessory — whoever clicks on during a running fetch otherwise gets the
 * answer of the page they left written into the new one.
 */
export interface AsyncState<T> {
  /** Is the first or a repeated fetch running? */
  pending: boolean;
  /** The last answer, or `null` as long as there is none. */
  data: T | null;
  /** Ask again — after a change that shifts the answer. */
  reload: () => void;
}

export function useAsync<T>(
  load: (signal: AbortSignal) => Promise<T>,
  deps: DependencyList,
  active = true
): AsyncState<T> {
  const [data, setData] = useState<T | null>(null);
  const [pending, setPending] = useState(active);
  const [round, setRound] = useState(0);

  // The function changes identity on every render; if it stood in the
  // dependencies, the fetch would run endlessly. The caller names instead the
  // values the answer really depends on.
  const loadRef = useRef(load);
  loadRef.current = load;

  useEffect(() => {
    if (!active) {
      setPending(false);
      return;
    }
    const abort = new AbortController();
    let alive = true;
    setPending(true);
    void loadRef.current(abort.signal).then(
      (value) => {
        if (!alive) return;
        setData(value);
        setPending(false);
      },
      () => {
        // The clients do not throw; if something arrives here anyway, it is the
        // abort itself. A hanging loading state would be the worse consequence.
        if (alive) setPending(false);
      }
    );
    return () => {
      alive = false;
      abort.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, round, active]);

  return { pending, data, reload: () => setRound((n) => n + 1) };
}

/**
 * A list that pages on at the cursor — and keeps the previous pages.
 *
 * Keeping is the actual promise: whoever presses "load more" wants to get more,
 * not get something replaced. If a later page fails, the earlier ones stay —
 * they were real.
 *
 * `key` is the search as a string. If it changes, the list starts over: new
 * cursor, empty collection, new loading state.
 */
export interface Page<T> {
  items: T[];
  nextCursor: string | null;
}

export type PageFetch<T, F> = ({ ok: true } & Page<T>) | { ok: false; error: F };

export interface Paging<T, F> {
  items: T[];
  pending: boolean;
  error: F | null;
  /** Is there a further page? */
  hasMore: boolean;
  loadMore: () => void;
}

interface PagingState<T, F> {
  key: string;
  cursor: string | undefined;
  items: T[];
  next: string | null;
  pending: boolean;
  error: F | null;
}

export function usePages<T, F>(
  load: (cursor: string | undefined, signal: AbortSignal) => Promise<PageFetch<T, F>>,
  key: string,
  active = true
): Paging<T, F> {
  const empty = (forKey: string): PagingState<T, F> => ({
    key: forKey,
    cursor: undefined,
    items: [],
    next: null,
    pending: active,
    error: null,
  });

  const [state, setState] = useState<PagingState<T, F>>(() => empty(key));

  // Reset DURING the render, not in an effect. An effect would only run after
  // the commit — the fetch effect would then already have fired once with the
  // old cursor and the new key, and two requests would race for the same list.
  // React discards the running render and starts again; one extra pass, no
  // extra request.
  if (state.key !== key) setState(empty(key));

  const loadRef = useRef(load);
  loadRef.current = load;

  // The key the STATE carries — not the one asked for. During a switch those
  // are two different ones, and the check further down rests on exactly that.
  const stateKey = state.key;
  const cursor = state.cursor;

  useEffect(() => {
    if (!active) return;
    const abort = new AbortController();
    let alive = true;
    void loadRef.current(cursor, abort.signal).then(
      (result) => {
        if (!alive) return;
        setState((previous) => {
          if (previous.key !== stateKey || previous.cursor !== cursor) return previous;
          if (!result.ok) return { ...previous, pending: false, error: result.error };
          return {
            ...previous,
            pending: false,
            error: null,
            // Without a cursor it is a new search; with a cursor it adds on.
            items: cursor === undefined ? result.items : [...previous.items, ...result.items],
            next: result.nextCursor,
          };
        });
      },
      () => {
        if (alive) setState((previous) => ({ ...previous, pending: false }));
      }
    );
    return () => {
      alive = false;
      abort.abort();
    };
  }, [stateKey, cursor, active]);

  return {
    items: state.items,
    pending: state.pending,
    error: state.error,
    hasMore: state.next !== null,
    loadMore: () =>
      setState((previous) =>
        previous.next === null ? previous : { ...previous, cursor: previous.next, pending: true }
      ),
  };
}
