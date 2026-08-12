import { afterEach, describe, expect, it, vi } from "vitest";

import { PROFILE_VISIBILITY, isGranted } from "./client";

afterEach(() => vi.restoreAllMocks());

const SUBJECT = "11111111-1111-1111-1111-111111111111";

/**
 * `isGranted` hatte keinen Test — und genau die Zeile, die hier geprüft wird,
 * verschluckte vorher den Unterschied zwischen „nicht freigegeben" und „der
 * Ledger hat nicht geantwortet". Beides war `false`, und der Aufrufer konnte es
 * nicht mehr auseinanderhalten.
 */
describe("isGranted", () => {
  it("sagt ja, wenn der Ledger ja sagt", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(JSON.stringify({ granted: true }), { status: 200 }))
    );

    expect(await isGranted(SUBJECT, PROFILE_VISIBILITY)).toBe(true);
  });

  it("sagt nein, wenn der Ledger nein sagt", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(JSON.stringify({ granted: false }), { status: 200 }))
    );

    expect(await isGranted(SUBJECT, PROFILE_VISIBILITY)).toBe(false);
  });

  it("sagt nein zu einer gelöschten Einwilligung, auch wenn sie einmal galt", async () => {
    // Eine Kontolöschung hängt `DELETE` an die Kette. „Wurde einmal gewährt"
    // ist dann keine Erlaubnis mehr.
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(JSON.stringify({ granted: true, deleted: true }), { status: 200 }))
    );

    expect(await isGranted(SUBJECT, PROFILE_VISIBILITY)).toBe(false);
  });

  it("sagt „weiß nicht“, wenn der Ledger einen Fehler meldet — nicht „nein“", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response("kaputt", { status: 503 })));

    expect(await isGranted(SUBJECT, PROFILE_VISIBILITY)).toBeNull();
  });

  it("sagt „weiß nicht“, wenn die Verbindung fehlschlägt", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => {
        throw new Error("kein Netz");
      })
    );

    expect(await isGranted(SUBJECT, PROFILE_VISIBILITY)).toBeNull();
  });

  it("sagt „weiß nicht“ auch bei einer Antwort ohne verwertbaren Inhalt", async () => {
    // 200 mit Müll im Körper: `res.json()` wirft, und das ist kein „nein".
    vi.stubGlobal("fetch", vi.fn(async () => new Response("kein json", { status: 200 })));

    expect(await isGranted(SUBJECT, PROFILE_VISIBILITY)).toBeNull();
  });
});
