import { useCallback, useEffect, useState } from "react";

/**
 * Loading without TanStack Query.
 *
 * <strong>Measured, not chosen.</strong> `AppRoot.tsx` mounts no
 * `QueryClientProvider` — `useQuery` from the old code would throw in the real
 * application ("No QueryClient set"). The OLD test helper provided one, so a
 * test would have been green while the page stays white.
 *
 * A `personSlice` is ruled out as well: `core/store/store.ts` registers `auth`
 * and `preferences` and lies outside this territory.
 *
 * The hook knows three states, and that is the point: `loading` is not
 * "empty", and `value === undefined` is not "nothing there".
 */
export interface AsyncState<T> {
  value: T | undefined;
  loading: boolean;
  /** Load again — after a change that altered the answer. */
  again: () => void;
  /** Set the value by hand when the answer already brought it along. */
  set: (value: T) => void;
}

/**
 * @param load calls the service. Receives an `AbortSignal`.
 * @param active `false` means "do not even ask" (for example: nobody signed
 *   in). The state then stays loading for ever — the page checks beforehand.
 */
export function useAsync<T>(
  load: (signal: AbortSignal) => Promise<T>,
  deps: unknown[],
  active = true
): AsyncState<T> {
  const [value, setValue] = useState<T | undefined>(undefined);
  const [loading, setLoading] = useState(active);
  const [round, setRound] = useState(0);

  // The dependencies come from the caller; `load` itself is a new function on
  // every render and must therefore NOT go into the list.
  const call = useCallback(load, deps);

  useEffect(() => {
    if (!active) {
      setLoading(false);
      return;
    }
    const abort = new AbortController();
    let alive = true;
    setLoading(true);
    void call(abort.signal).then(
      (answer) => {
        if (!alive) return;
        setValue(answer);
        setLoading(false);
      },
      () => {
        // An aborted call is no result. The API modules do not throw anyway —
        // a status code is an answer there.
        if (alive) setLoading(false);
      }
    );
    return () => {
      alive = false;
      abort.abort();
    };
  }, [call, active, round]);

  return {
    value,
    loading,
    again: useCallback(() => setRound((n) => n + 1), []),
    set: useCallback((next: T) => {
      setValue(next);
      setLoading(false);
    }, []),
  };
}
