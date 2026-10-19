/**
 * Letter data that a page switch does not fetch again.
 *
 * Postal address, CV and company profile do not change while the model writes.
 * Asking for them on every mount produces the 404s in the console
 * (`/resumes/me`, `/companies/…/profile`) and aborts running fetches (499).
 *
 * ## Discarding must take the running fetch with it
 *
 * Until 10.09.2026 `forget` and `forgetContext` only emptied the maps — a fetch
 * already on its way kept running and wrote its result in **afterwards**. So
 * whoever changed their CV while the old fetch was still running got the old
 * state back into the cache, and the page showed it until the next hard reload.
 *
 * The test suite found it, on the runner rather than here: `DraftPage` failed
 * there at "Lebenslauf bearbeiten", because an answer from the previous case
 * arrived after the `afterEach` and filled the cache again. Locally the fetch
 * was always done before — five runs green. A higher wait limit changed nothing
 * about it and could not: it was never too slow, it was the wrong value.
 *
 * The state is therefore counted **per key**. A fetch writes only if nobody has
 * discarded the same key since it started. A global counter would be shorter
 * and worse: every `forget` would also throw away the fetches of all other keys.
 */

const values = new Map<string, unknown>();
const inFlight = new Map<string, Promise<unknown>>();

/**
 * How often this key was discarded.
 *
 * Deliberately NEVER emptied, not even by `forgetContext`: a fetch still on its
 * way must be able to recognise its state after the emptying. The set of keys
 * is small and fixed (CV, postal address, job, company, profile), so nothing
 * grows.
 */
const discards = new Map<string, number>();

function discardsOf(key: string): number {
  return discards.get(key) ?? 0;
}

function discard(key: string): void {
  discards.set(key, discardsOf(key) + 1);
  values.delete(key);
  inFlight.delete(key);
}

export function remember<T>(
  key: string,
  load: () => Promise<T>,
): Promise<T> {
  if (values.has(key)) {
    return Promise.resolve(values.get(key) as T);
  }

  const running = inFlight.get(key) as Promise<T> | undefined;
  if (running) return running;

  const mine = discardsOf(key);

  const work = load().then(
    (value) => {
      // ONLY WRITE IF NOBODY DISCARDED IN BETWEEN. Without this check a late
      // fetch undoes every `forget`.
      if (discardsOf(key) === mine) {
        values.set(key, value);
        inFlight.delete(key);
      }
      return value;
    },
    (error: unknown) => {
      if (discardsOf(key) === mine) {
        inFlight.delete(key);
      }
      throw error;
    },
  );

  inFlight.set(key, work);
  return work;
}

/** Discard an answer that has changed on the page. */
export function forget(key: string): void {
  discard(key);
}

/** For tests only: otherwise a 404 from the previous case hangs on. */
export function forgetContext(): void {
  for (const key of new Set([...values.keys(), ...inFlight.keys()])) {
    discard(key);
  }
  values.clear();
  inFlight.clear();
}
