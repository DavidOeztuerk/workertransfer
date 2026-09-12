import { afterEach, describe, expect, it, vi } from "vitest";

import { GLEICHZEITIG, starteEntwuerfe } from "./entwuerfe";
import * as api from "../api/applications";
import type { Draft } from "../api/applications";

/**
 * Die Begrenzung und der Fortschritt — beide ohne Uhr geprüft.
 *
 * <strong>Nicht über Zeit gemessen.</strong> Ein Test, der drei parallele
 * Aufrufe an einer Wartezeit erkennt, ist auf einem langsamen Läufer rot und
 * sagt dann nichts über die Begrenzung. Hier entscheidet der Test selbst, wann
 * ein Schreibauftrag fertig wird: er zählt die gleichzeitig offenen und lässt
 * sie einzeln los. Derselbe Grund wie in `kontext.test.ts`.
 */

function entwurf(n: number): Draft {
  return {
    id: `00000000-0000-4000-8000-${String(n).padStart(12, "0")}`,
    job_id: `job-${n}`,
    status: "generating",
  } as Draft;
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe("starteEntwuerfe", () => {
  /** Ein Steuerpult: jeder Schreibauftrag hängt, bis er einzeln gelöst wird. */
  function steuerpult(anzahl: number) {
    const loesen: Array<() => void> = [];
    let offen = 0;
    let hoechststand = 0;

    vi.spyOn(api, "createDrafts").mockResolvedValue({
      ok: true,
      drafts: Array.from({ length: anzahl }, (_, i) => entwurf(i)),
    } as Awaited<ReturnType<typeof api.createDrafts>>);

    vi.spyOn(api, "writeDraft").mockImplementation(() => {
      offen += 1;
      hoechststand = Math.max(hoechststand, offen);
      return new Promise((fertig) => {
        loesen.push(() => {
          offen -= 1;
          fertig({ ok: true, draft: entwurf(0) } as Awaited<
            ReturnType<typeof api.writeDraft>
          >);
        });
      });
    });

    return {
      loesen,
      hoechststand: () => hoechststand,
      /** Einen laufenden Auftrag abschliessen und die Schlange nachziehen lassen. */
      async einen() {
        loesen.shift()?.();
        await Promise.resolve();
        await Promise.resolve();
      },
    };
  }

  /*
   * Die DREI steht hier als Zahl und nicht nur als `GLEICHZEITIG`.
   *
   * Gemessen an der ersten Fassung dieses Tests: er verglich allein gegen die
   * Konstante, und die Gegenprobe `GLEICHZEITIG = 12` liess ihn grün. Ein Test,
   * der sich seinen Massstab aus dem Prüfling holt, prüft nichts — die Zusage
   * aus dem Auftrag lautet „max. 3", nicht „so viele wie die Konstante sagt".
   */
  it("nennt drei als Grenze", () => {
    expect(GLEICHZEITIG).toBe(3);
  });

  it("schreibt höchstens drei gleichzeitig", async () => {
    const pult = steuerpult(12);

    const lauf = starteEntwuerfe(Array.from({ length: 12 }, (_, i) => `job-${i}`));
    await Promise.resolve();
    await Promise.resolve();

    expect(pult.hoechststand()).toBe(3);

    // Zwölf einzeln durchlassen — der Höchststand darf dabei nie steigen.
    for (let i = 0; i < 12; i += 1) {
      await pult.einen();
      expect(pult.hoechststand()).toBe(3);
    }

    await expect(lauf).resolves.toMatchObject({ ok: true });
  });

  /*
   * Der Platz wird NACHGEZOGEN, nicht blockweise freigegeben. Eine
   * `chunk(3)`-Umsetzung bestünde den Test darüber — sie hält die Grenze
   * ebenfalls ein —, fiele aber hier: nach genau EINEM fertigen Auftrag
   * stünde bei ihr noch kein vierter an, weil sie auf den ganzen Block wartet.
   */
  it("zieht nach, sobald ein Platz frei wird", async () => {
    const pult = steuerpult(6);

    const lauf = starteEntwuerfe(Array.from({ length: 6 }, (_, i) => `job-${i}`));
    await Promise.resolve();
    await Promise.resolve();

    expect(api.writeDraft).toHaveBeenCalledTimes(3);
    await pult.einen();
    expect(api.writeDraft).toHaveBeenCalledTimes(4);

    for (let i = 0; i < 6; i += 1) await pult.einen();
    await expect(lauf).resolves.toMatchObject({ ok: true });
  });

  it("meldet den Fortschritt von null bis vollständig", async () => {
    const pult = steuerpult(4);
    const gemeldet: Array<[number, number]> = [];

    const lauf = starteEntwuerfe(
      Array.from({ length: 4 }, (_, i) => `job-${i}`),
      (fertig, gesamt) => gemeldet.push([fertig, gesamt]),
    );
    await Promise.resolve();

    // Die Null steht VOR dem ersten fertigen Entwurf da — sonst springt die
    // Leiste aus dem Nichts auf „1 von 4".
    expect(gemeldet[0]).toEqual([0, 4]);

    for (let i = 0; i < 4; i += 1) await pult.einen();
    await lauf;

    expect(gemeldet).toEqual([
      [0, 4],
      [1, 4],
      [2, 4],
      [3, 4],
      [4, 4],
    ]);
  });

  it("meldet nichts und schreibt nichts, wenn das Anlegen scheitert", async () => {
    vi.spyOn(api, "createDrafts").mockResolvedValue({
      ok: false,
      reason: "fehlgeschlagen",
      error: { status: 500, title: "kaputt", detail: "kaputt" },
    } as Awaited<ReturnType<typeof api.createDrafts>>);
    const schreiben = vi.spyOn(api, "writeDraft");
    const gemeldet: Array<[number, number]> = [];

    const start = await starteEntwuerfe(["job-0"], (f, g) => gemeldet.push([f, g]));

    expect(start).toEqual({ ok: false, drafts: [], fehler: "kaputt" });
    expect(schreiben).not.toHaveBeenCalled();
    expect(gemeldet).toEqual([]);
  });
});
