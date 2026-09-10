import { afterEach, describe, expect, it } from "vitest";

import { merke, vergiss, vergissKontext } from "./kontext";

afterEach(() => {
  vergissKontext();
});

/** Ein Abruf, dessen Antwortzeitpunkt der Test bestimmt. */
function steuerbar<T>() {
  let loese!: (wert: T) => void;
  const versprechen = new Promise<T>((erfuellen) => {
    loese = erfuellen;
  });
  return { versprechen, loese };
}

describe("kontext", () => {
  it("merkt sich eine Antwort und fragt kein zweites Mal", async () => {
    let gefragt = 0;

    const erste = await merke("k", () => {
      gefragt += 1;
      return Promise.resolve("wert");
    });
    const zweite = await merke("k", () => {
      gefragt += 1;
      return Promise.resolve("anderer");
    });

    expect(erste).toBe("wert");
    expect(zweite).toBe("wert");
    expect(gefragt).toBe(1);
  });

  it("bündelt zwei gleichzeitige Fragen zu einem Abruf", async () => {
    let gefragt = 0;
    const laden = () => {
      gefragt += 1;
      return Promise.resolve("wert");
    };

    const [a, b] = await Promise.all([merke("k", laden), merke("k", laden)]);

    expect(a).toBe("wert");
    expect(b).toBe("wert");
    expect(gefragt).toBe(1);
  });

  // DER FALL, DER AUF DEM LAEUFER ROT WAR UND HIER NIE.
  //
  // Ein Abruf ist unterwegs, jemand verwirft, und ERST DANN antwortet er. Bis
  // zum 10.09.2026 schrieb er seinen Wert trotzdem hinein und machte das
  // Verwerfen rueckgaengig — in `DraftPage` hiess der Knopf danach
  // "Lebenslauf anlegen" statt "Lebenslauf bearbeiten".
  //
  // Der Test haengt an keiner Wartezeit: er bestimmt selbst, wann die alte
  // Antwort kommt.
  it("eine Antwort, die nach dem Verwerfen kommt, wird nicht mehr gespeichert", async () => {
    const alt = steuerbar<string>();

    const unterwegs = merke("k", () => alt.versprechen);

    vergiss("k");

    alt.loese("alt");
    await unterwegs;

    const jetzt = await merke("k", () => Promise.resolve("neu"));

    expect(jetzt).toBe("neu");
  });

  it("dasselbe gilt für vergissKontext", async () => {
    const alt = steuerbar<string>();

    const unterwegs = merke("k", () => alt.versprechen);

    vergissKontext();

    alt.loese("alt");
    await unterwegs;

    expect(await merke("k", () => Promise.resolve("neu"))).toBe("neu");
  });

  it("verwirft nur den genannten Schlüssel, nicht die anderen", async () => {
    const a = steuerbar<string>();
    const b = steuerbar<string>();

    const laufA = merke("a", () => a.versprechen);
    const laufB = merke("b", () => b.versprechen);

    vergiss("a");

    a.loese("a-alt");
    b.loese("b-wert");
    await Promise.all([laufA, laufB]);

    // `b` war nie gemeint und muss deshalb gespeichert sein.
    expect(await merke("b", () => Promise.resolve("b-neu"))).toBe("b-wert");
    expect(await merke("a", () => Promise.resolve("a-neu"))).toBe("a-neu");
  });

  it("ein Fehler wird nicht gemerkt — die nächste Frage darf es erneut versuchen", async () => {
    await expect(
      merke("k", () => Promise.reject(new Error("kaputt"))),
    ).rejects.toThrow("kaputt");

    expect(await merke("k", () => Promise.resolve("wert"))).toBe("wert");
  });
});
