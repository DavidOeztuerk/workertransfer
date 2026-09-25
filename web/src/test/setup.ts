import "@testing-library/jest-dom/vitest";
import { configure } from "@testing-library/react";

/**
 * The wait limit of `findBy*` and `waitFor` — 1000 ms would be too tight.
 *
 * It is NOT the same as Vitest's `testTimeout` (20 s in vite.config.ts, for
 * the same reason): `testTimeout` ends the whole test, `asyncUtilTimeout` ends
 * a single wait. A test can therefore run for 3.8 seconds and still fail on a
 * one-second wait — exactly that was the finding: a full run with 2 red tests,
 * the next with 7, different files each time, and all green as soon as they ran
 * alone.
 *
 * A test suite whose result depends on machine load is none. The higher limit
 * slows no green run down: it only takes effect where the run used to abort.
 *
 * BACK TO 5 SECONDS. On 09.09.2026 it stood at 10, on the grounds that
 * `DraftPage` failed on the runner because of a fetch chain that was too long.
 * That was wrong: the increase changed nothing, and the test fell the same way
 * in the next run. The cause lay in `features/work/lib/kontext.ts` — an answer
 * that arrived after the discard filled the cache again. It was never too
 * slow, it was the wrong value.
 *
 * The lesson is the number itself: raising a wait limit always looks like a
 * fix and just as often hides one.
 */
configure({ asyncUtilTimeout: 5_000 });

/**
 * A `localStorage` of our own, predictable, for the test run.
 *
 * Not for convenience, but because it otherwise depends on the Node version:
 * **Node 25 ships a global `localStorage` that has no methods without
 * `--localstorage-file`.** jsdom takes it over, and
 * `window.localStorage.getItem` is then `undefined`. Locally on Node 24 that
 * went unnoticed — in CI (Node 25) 18 tests in five files fell over with
 * `TypeError: s.getItem is not a function`.
 *
 * The alternative would have been to insert the replacement only when the
 * existing one is broken. Then CI would check something other than the
 * development machine — and in the worse case nothing at all, because the
 * production code silently skips an unusable storage and every test would be
 * trivially green. One replacement for all is more honest.
 *
 * The production code protects itself independently of this
 * (`core/store/preferencesSlice.ts`): it catches every access and otherwise
 * does without. `intent.ts` — the remembered location — stood here, and it
 * fell with PBI-8 because it was written and never read.
 */
function inMemoryStorage(): Storage {
  const data = new Map<string, string>();
  return {
    get length() {
      return data.size;
    },
    clear: () => data.clear(),
    getItem: (key: string) => data.get(key) ?? null,
    key: (index: number) => [...data.keys()][index] ?? null,
    removeItem: (key: string) => void data.delete(key),
    setItem: (key: string, value: string) => void data.set(key, String(value)),
  };
}

for (const target of [globalThis, globalThis.window].filter(Boolean)) {
  try {
    Object.defineProperty(target, "localStorage", {
      value: inMemoryStorage(),
      configurable: true,
      writable: true,
    });
  } catch {
    // If the entry cannot be overwritten, the existing one stays. Whether it
    // is any good is decided by the check below.
  }
}

// A self-check, with one sentence instead of eighteen riddles.
//
// Without it a storage that cannot be overwritten shows up as `TypeError:
// s.getItem is not a function` in five different files — exactly the picture
// this file clears up, and one searches for it in the production code. A clear
// abort here names the cause instead.
if (typeof globalThis.localStorage?.getItem !== "function") {
  throw new Error(
    "localStorage is unusable in this test environment and could not be " +
      "replaced. Known cause: Node >= 25 provides a global localStorage " +
      "without methods (usable only with --localstorage-file). " +
      "See web/src/test/setup.ts."
  );
}
