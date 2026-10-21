import { type MessageKey, failureKey } from "../../../shared/api/errors";
import { getMyDocuments } from "../../person/api/resume";
import type { Draft } from "../api/applications";
import { chooseAttachments, createDrafts, writeDraft } from "../api/applications";

/**
 * Turn jobs into drafts — the one place in the browser that does it.
 *
 * <strong>The model writes only when asked.</strong> `POST /applications/drafts`
 * creates open drafts; `write: true` then starts the writing per draft via
 * <c>/write</c>. A writing costs the person's AI credit (GESCHAEFTSMODELL C),
 * so it is never the default — and per draft, so every one stays visible on
 * its own instead of a bulk call silently swallowing half of them.
 *
 * <strong>Every uploaded document goes along by default.</strong> Upload once,
 * enclose everywhere; a document is deselected per application, not chosen
 * each time. Only a fresh draft is filled — one the person already shaped
 * keeps her choice.
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
  /**
   * What went wrong, as a catalogue KEY — never the server's sentence, which
   * is English on the wire. Until 26.09.2026 the problem document's `detail`
   * stood here, and both callers drew it raw.
   */
  error: MessageKey | null;
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
  { write = false, report }: { write?: boolean; report?: ProgressReporter } = {},
): Promise<DraftStart> {
  const result = await createDrafts(jobIds);
  if (!result.ok) {
    return { ok: false, drafts: [], error: failureKey(result.error, "errors.draftsNotCreated") };
  }

  const drafts = result.drafts;
  await encloseEverything(drafts);
  if (!write) return { ok: true, drafts, error: null };

  const open = drafts.filter(
    (draft) => draft.status === "open" || draft.status === "failed",
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

/**
 * All of the person's documents onto every fresh draft.
 *
 * A failure here costs nothing but convenience: the draft stands, and the
 * documents can still be ticked on its page. It is therefore not reported.
 */
async function encloseEverything(drafts: Draft[]): Promise<void> {
  const fresh = drafts.filter(
    (draft) => draft.status === "open" && draft.documents.length === 0 && draft.body.trim() === "",
  );
  if (fresh.length === 0) return;

  const documents = await getMyDocuments();
  if (!documents.ok || documents.value.length === 0) return;

  const ids = documents.value.map((document) => document.id);
  await Promise.all(fresh.map((draft) => chooseAttachments(draft.id, true, ids)));
}
