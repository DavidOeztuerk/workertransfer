import type { Draft } from "../api/applications";
import { createDrafts, writeDraft } from "../api/applications";

/**
 * Turn jobs into drafts — the one place in the browser that does it.
 *
 * <strong>Two calls and not one.</strong> `POST /applications/drafts` only
 * creates (state <c>generating</c>); writing happens per draft via
 * <c>/write</c>. So every draft stays visible on its own, instead of a bulk
 * call silently swallowing half of them.
 *
 * <strong>Why this lives here and not in a page.</strong> There were two ways
 * to an application — the job list and the career page — and the second went
 * through a page of its own, <c>/jobs/:id/apply</c>, that did nothing but
 * repeat exactly this and then redirect. A switch with its own address is a
 * second truth about the same process; the address has fallen, the process
 * stands here.
 */
export interface DraftStart {
  ok: boolean;
  drafts: Draft[];
  /** The server's sentence when it went wrong. */
  error: string | null;
}

/**
 * At most three writing jobs at the same time.
 *
 * `Promise.all` over ALL chosen jobs used to stand here: whoever ticked twelve
 * sent twelve jobs at once to applications-service and from there to the AI
 * provider — both places where one person alone creates a queue for everybody
 * else. The number is the one from the assignment and no measured
 * optimisation; it may rise when somebody measures.
 */
export const CONCURRENCY = 3;

/**
 * How many of how many are written.
 *
 * Called after EVERY finished draft, and once with `0` beforehand — without the
 * first call the bar only appears after the first draft and then jumps out of
 * nowhere to "1 of 12".
 */
export type ProgressReporter = (done: number, total: number) => void;

export async function startDrafts(
  jobIds: string[],
  report?: ProgressReporter,
): Promise<DraftStart> {
  const result = await createDrafts(jobIds);
  if (!result.ok) return { ok: false, drafts: [], error: result.error.detail };

  const drafts = result.drafts;
  const open = drafts.filter(
    (draft) => draft.status === "generating" || draft.status === "failed",
  );

  report?.(0, open.length);

  /*
   * One queue and three workers, not three blocks one after the other.
   *
   * `chunk(3)` would be shorter and slower: a block waits for its slowest draft
   * before the next one starts — with a provider that takes a different time
   * per request, two of three slots would stand empty for a large part of the
   * time. Here every worker pulls the next one as soon as it is done.
   */
  let next = 0;
  let done = 0;

  async function work(): Promise<void> {
    for (;;) {
      const own = next;
      next += 1;
      const draft = open[own];
      if (draft === undefined) return;

      // The result is NOT checked: a single failed draft stands at `failed`
      // afterwards and is visible on the drafts page — cancelling the run
      // because of it would take their text away from the other eleven.
      await writeDraft(draft.id);
      done += 1;
      report?.(done, open.length);
    }
  }

  await Promise.all(
    Array.from({ length: Math.min(CONCURRENCY, open.length) }, () => work()),
  );

  return { ok: true, drafts, error: null };
}
