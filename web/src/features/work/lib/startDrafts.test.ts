import { afterEach, describe, expect, it, vi } from "vitest";

import { CONCURRENCY, startDrafts } from "./startDrafts";
import * as api from "../api/applications";
import * as resume from "../../person/api/resume";
import type { Draft } from "../api/applications";

/**
 * The limit and the progress — both checked without a clock.
 *
 * <strong>Not measured by time.</strong> A test that recognises three parallel
 * calls by a waiting time is red on a slow runner and then says nothing about
 * the limit. Here the test itself decides when a writing job finishes: it
 * counts the ones open at the same time and releases them one by one. The same
 * reason as in `letterContext.test.ts`.
 */

function draft(n: number, documents: string[] = ["already-chosen"]): Draft {
  return {
    id: `00000000-0000-4000-8000-${String(n).padStart(12, "0")}`,
    job_id: `job-${n}`,
    status: "open",
    body: "",
    documents,
  } as Draft;
}

/** Every pending microtask and the next turn — independent of how many awaits there are. */
const flush = () => new Promise((resolve) => setTimeout(resolve, 0));

afterEach(() => {
  vi.restoreAllMocks();
});

describe("startDrafts", () => {
  /** A control desk: every writing job hangs until it is released on its own. */
  function controlDesk(count: number) {
    const release: Array<() => void> = [];
    let open = 0;
    let peak = 0;

    vi.spyOn(api, "createDrafts").mockResolvedValue({
      ok: true,
      drafts: Array.from({ length: count }, (_, i) => draft(i)),
    } as Awaited<ReturnType<typeof api.createDrafts>>);

    vi.spyOn(api, "writeDraft").mockImplementation(() => {
      open += 1;
      peak = Math.max(peak, open);
      return new Promise((resolve) => {
        release.push(() => {
          open -= 1;
          resolve({ ok: true, draft: draft(0) } as Awaited<
            ReturnType<typeof api.writeDraft>
          >);
        });
      });
    });

    return {
      release,
      peak: () => peak,
      /** Finish one running job and let the queue pull the next. */
      async one() {
        release.shift()?.();
        await Promise.resolve();
        await Promise.resolve();
      },
    };
  }

  /*
   * The THREE stands here as a number and not only as `CONCURRENCY`.
   *
   * Measured on the first version of this test: it compared against the
   * constant alone, and the counter-probe `CONCURRENCY = 12` left it green. A
   * test that takes its yardstick from the thing under test checks nothing —
   * the promise from the assignment reads "max. 3", not "as many as the
   * constant says".
   */
  it("names three as the limit", () => {
    expect(CONCURRENCY).toBe(3);
  });

  it("writes at most three at the same time", async () => {
    const desk = controlDesk(12);

    const run = startDrafts(Array.from({ length: 12 }, (_, i) => `job-${i}`), { write: true });
    await flush();

    expect(desk.peak()).toBe(3);

    // Let twelve through one by one — the peak must never rise meanwhile.
    for (let i = 0; i < 12; i += 1) {
      await desk.one();
      expect(desk.peak()).toBe(3);
    }

    await expect(run).resolves.toMatchObject({ ok: true });
  });

  /*
   * The slot is PULLED, not released block by block. A `chunk(3)`
   * implementation would pass the test above — it keeps the limit too — but
   * would fail here: after exactly ONE finished job no fourth would be queued
   * with it yet, because it waits for the whole block.
   */
  it("pulls the next one as soon as a slot is free", async () => {
    const desk = controlDesk(6);

    const run = startDrafts(Array.from({ length: 6 }, (_, i) => `job-${i}`), { write: true });
    await flush();

    expect(api.writeDraft).toHaveBeenCalledTimes(3);
    await desk.one();
    expect(api.writeDraft).toHaveBeenCalledTimes(4);

    for (let i = 0; i < 6; i += 1) await desk.one();
    await expect(run).resolves.toMatchObject({ ok: true });
  });

  it("reports the progress from zero to complete", async () => {
    const desk = controlDesk(4);
    const reported: Array<[number, number]> = [];

    const run = startDrafts(Array.from({ length: 4 }, (_, i) => `job-${i}`), {
      write: true,
      report: (done, total) => reported.push([done, total]),
    });
    await flush();

    // The zero stands there BEFORE the first finished draft — otherwise the bar
    // jumps out of nowhere to "1 of 4".
    expect(reported[0]).toEqual([0, 4]);

    for (let i = 0; i < 4; i += 1) await desk.one();
    await run;

    expect(reported).toEqual([
      [0, 4],
      [1, 4],
      [2, 4],
      [3, 4],
      [4, 4],
    ]);
  });

  it("reports nothing and writes nothing when creating fails", async () => {
    vi.spyOn(api, "createDrafts").mockResolvedValue({
      ok: false,
      reason: "failed",
      error: { status: 500, title: "Request failed", detail: "internal error" },
    } as Awaited<ReturnType<typeof api.createDrafts>>);
    const write = vi.spyOn(api, "writeDraft");
    const reported: Array<[number, number]> = [];

    const start = await startDrafts(["job-0"], {
      write: true,
      report: (done, total) => reported.push([done, total]),
    });

    // A catalogue key, never the server's English sentence.
    expect(start).toEqual({ ok: false, drafts: [], error: "errors.draftsNotCreated" });
    expect(write).not.toHaveBeenCalled();
    expect(reported).toEqual([]);
  });

  /** A writing costs the person's AI credit — selecting jobs spends nothing. */
  it("writes nothing unless asked", async () => {
    vi.spyOn(api, "createDrafts").mockResolvedValue({
      ok: true,
      drafts: [draft(0), draft(1)],
    } as Awaited<ReturnType<typeof api.createDrafts>>);
    const write = vi.spyOn(api, "writeDraft");

    const start = await startDrafts(["job-0", "job-1"]);

    expect(start.ok).toBe(true);
    expect(write).not.toHaveBeenCalled();
  });

  /** Upload once, enclose everywhere — but a draft already shaped keeps its choice. */
  it("encloses every document on a fresh draft only", async () => {
    vi.spyOn(api, "createDrafts").mockResolvedValue({
      ok: true,
      drafts: [draft(0, []), draft(1, ["kept"])],
    } as Awaited<ReturnType<typeof api.createDrafts>>);
    vi.spyOn(resume, "getMyDocuments").mockResolvedValue({
      ok: true,
      value: [{ id: "cv" }, { id: "certificate" }],
    } as Awaited<ReturnType<typeof resume.getMyDocuments>>);
    const choose = vi
      .spyOn(api, "chooseAttachments")
      .mockResolvedValue({ ok: true } as Awaited<ReturnType<typeof api.chooseAttachments>>);

    await startDrafts(["job-0", "job-1"]);

    expect(choose).toHaveBeenCalledTimes(1);
    expect(choose).toHaveBeenCalledWith(draft(0).id, true, ["cv", "certificate"]);
  });
});
