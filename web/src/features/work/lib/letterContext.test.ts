import { afterEach, describe, expect, it } from "vitest";

import { forget, forgetContext, remember } from "./letterContext";

afterEach(() => {
  forgetContext();
});

/** A fetch whose moment of answering the test decides. */
function controllable<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((fulfil) => {
    resolve = fulfil;
  });
  return { promise, resolve };
}

describe("letterContext", () => {
  it("remembers an answer and does not ask a second time", async () => {
    let asked = 0;

    const first = await remember("k", () => {
      asked += 1;
      return Promise.resolve("value");
    });
    const second = await remember("k", () => {
      asked += 1;
      return Promise.resolve("other");
    });

    expect(first).toBe("value");
    expect(second).toBe("value");
    expect(asked).toBe(1);
  });

  it("bundles two simultaneous questions into one fetch", async () => {
    let asked = 0;
    const load = () => {
      asked += 1;
      return Promise.resolve("value");
    };

    const [a, b] = await Promise.all([remember("k", load), remember("k", load)]);

    expect(a).toBe("value");
    expect(b).toBe("value");
    expect(asked).toBe(1);
  });

  // THE CASE THAT WAS RED ON THE RUNNER AND NEVER HERE.
  //
  // A fetch is on its way, somebody discards, and ONLY THEN does it answer.
  // Until 10.09.2026 it wrote its value in anyway and undid the discard — in
  // `DraftPage` the button then read "Lebenslauf anlegen" instead of
  // "Lebenslauf bearbeiten".
  //
  // The test hangs on no waiting time: it decides itself when the old answer
  // arrives.
  it("does not store an answer that arrives after the discard", async () => {
    const old = controllable<string>();

    const underway = remember("k", () => old.promise);

    forget("k");

    old.resolve("old");
    await underway;

    const now = await remember("k", () => Promise.resolve("new"));

    expect(now).toBe("new");
  });

  it("the same holds for forgetContext", async () => {
    const old = controllable<string>();

    const underway = remember("k", () => old.promise);

    forgetContext();

    old.resolve("old");
    await underway;

    expect(await remember("k", () => Promise.resolve("new"))).toBe("new");
  });

  it("discards only the named key, not the others", async () => {
    const a = controllable<string>();
    const b = controllable<string>();

    const runA = remember("a", () => a.promise);
    const runB = remember("b", () => b.promise);

    forget("a");

    a.resolve("a-old");
    b.resolve("b-value");
    await Promise.all([runA, runB]);

    // `b` was never meant and must therefore be stored.
    expect(await remember("b", () => Promise.resolve("b-new"))).toBe("b-value");
    expect(await remember("a", () => Promise.resolve("a-new"))).toBe("a-new");
  });

  it("does not remember an error — the next question may try again", async () => {
    await expect(
      remember("k", () => Promise.reject(new Error("kaputt"))),
    ).rejects.toThrow("kaputt");

    expect(await remember("k", () => Promise.resolve("value"))).toBe("value");
  });
});
