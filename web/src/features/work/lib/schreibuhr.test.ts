import { afterEach, describe, expect, it } from "vitest";

import { schreibbeginn, schreibende } from "./schreibuhr";

afterEach(() => {
  schreibende("a");
});

describe("schreibuhr", () => {
  it("behält den ersten Start über einen zweiten Aufruf", () => {
    const zuerst = schreibbeginn("a", null);
    const danach = schreibbeginn("a", null);
    expect(danach).toBe(zuerst);
  });

  it("nimmt den Serverstand, sobald er da ist", () => {
    schreibbeginn("a", null);
    expect(schreibbeginn("a", "2026-09-07T09:00:00.000Z")).toBe("2026-09-07T09:00:00.000Z");
  });
});
