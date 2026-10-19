import { useCallback, useEffect, useRef, useState } from "react";

/**
 * What an endpoint with page numbers returns.
 *
 * Without `hasNext`/`hasPrevious`, although the server sends them: they follow
 * necessarily from `page` and `totalPages`, and a field that follows from two
 * others is a second truth about the same thing. The bar computes them itself.
 */
export interface Page<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  /**
   * How many entries a filter could not judge and therefore left out. `null`
   * when the question did not arise.
   *
   * It stands here and not only with the jobs because it is a property of a
   * page and not of a domain: every list whose filter cannot say anything about
   * part of the set must be able to say so.
   */
  omitted?: number | null;
}

/** A result: either a page or a failure. */
export type PageResult<T, E> =
  | { ok: true; page: Page<T> }
  | { ok: false; error: E };

export interface PagedList<T, E> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  /** See `Page.omitted`. */
  omitted: number | null;
  pending: boolean;
  error: E | null;
  goTo: (page: number) => void;
  setPageSize: (pageSize: number) => void;
  retry: () => void;
}

/**
 * Paging by page numbers.
 *
 * <strong>The page replaces, it does not append.</strong> The predecessor
 * collected pages with "load more" — that suits a cursor and not a pagination
 * bar: whoever jumps to page 7 wants to see page 7 and not pages 1 to 7 one
 * below the other.
 *
 * <strong>A filter change resets to page 1</strong>, and it does so while
 * rendering instead of in an effect. An effect would run only after the
 * commit; the fetch would then have fired once with the new filter and the old
 * page — and whoever sets a filter on page 7 that has only three hits would see
 * an empty list and take it for the result.
 *
 * <strong>The previous page stays while the next one loads.</strong> Setting
 * the list to empty produces a jump of the whole page height at every click;
 * that is the difference between a tool and a flicker.
 */
export function usePagination<T, E>(
  load: (page: number, pageSize: number, signal: AbortSignal) => Promise<PageResult<T, E>>,
  key: string,
  defaultPageSize = 12,
  enabled = true
): PagedList<T, E> {
  const [page, setPage] = useState(1);
  const [pageSize, setPageSizeState] = useState(defaultPageSize);
  const [round, setRound] = useState(0);
  const [state, setState] = useState<{
    key: string;
    items: T[];
    totalItems: number;
    totalPages: number;
    omitted: number | null;
    pending: boolean;
    error: E | null;
  }>({
    key,
    items: [],
    totalItems: 0,
    totalPages: 1,
    omitted: null,
    pending: enabled,
    error: null,
  });

  // See above: WHILE rendering, not in the effect.
  if (state.key !== key) {
    setState({
      key,
      items: [],
      totalItems: 0,
      totalPages: 1,
      omitted: null,
      pending: enabled,
      error: null,
    });
    setPage(1);
  }

  const loadRef = useRef(load);
  loadRef.current = load;

  useEffect(() => {
    if (!enabled) return;

    const abort = new AbortController();
    let alive = true;

    setState((previous) => ({ ...previous, pending: true }));

    void loadRef.current(page, pageSize, abort.signal).then(
      (result) => {
        if (!alive) return;
        setState((previous) =>
          result.ok
            ? {
                ...previous,
                items: result.page.items,
                totalItems: result.page.totalItems,
                totalPages: result.page.totalPages,
                omitted: result.page.omitted ?? null,
                pending: false,
                error: null,
              }
            : { ...previous, pending: false, error: result.error }
        );
      },
      () => {
        // An abort is not a failure: it comes from the switch itself.
        if (alive && !abort.signal.aborted) {
          setState((previous) => ({ ...previous, pending: false }));
        }
      }
    );

    return () => {
      alive = false;
      abort.abort();
    };
  }, [key, page, pageSize, round, enabled]);

  const goTo = useCallback((target: number) => setPage(Math.max(1, target)), []);

  const setPageSize = useCallback((next: number) => {
    setPageSizeState(next);
    // A different size means a different division. Page 7 of twelve is not
    // page 7 of forty-eight — it might not exist at all.
    setPage(1);
  }, []);

  const retry = useCallback(() => setRound((n) => n + 1), []);

  return {
    items: state.items,
    page,
    pageSize,
    totalItems: state.totalItems,
    totalPages: state.totalPages,
    omitted: state.omitted,
    pending: state.pending,
    error: state.error,
    goTo,
    setPageSize,
    retry,
  };
}
