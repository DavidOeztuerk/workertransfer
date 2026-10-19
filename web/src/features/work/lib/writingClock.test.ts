import { afterEach, describe, expect, it } from "vitest";

import { writingEnded, writingStart } from "./writingClock";

afterEach(() => {
  writingEnded("a");
});

describe("writingClock", () => {
  it("keeps the first start across a second call", () => {
    const first = writingStart("a", null);
    const then = writingStart("a", null);
    expect(then).toBe(first);
  });

  it("takes the server's time as soon as it is there", () => {
    writingStart("a", null);
    expect(writingStart("a", "2026-09-07T09:00:00.000Z")).toBe("2026-09-07T09:00:00.000Z");
  });
});
