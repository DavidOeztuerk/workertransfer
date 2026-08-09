import { afterEach, describe, expect, it, vi } from "vitest";

import { gemerkteStelle, merkeStelle, vergissStelle } from "./intent";

const STELLE = "3f2a1c4e-9b7d-4e21-8a3f-1c2d3e4f5a6b";

afterEach(() => {
  // Attrappen ZUERST zurücknehmen: der Test, der localStorage werfen lässt,
  // liesse sonst das Aufräumen selbst scheitern — ein roter Test, der nichts
  // über den Code aussagt.
  vi.restoreAllMocks();
  vi.useRealTimers();
  window.localStorage.clear();
});

describe("gemerkte Stelle", () => {
  it("merkt und liefert sie zurück", () => {
    merkeStelle(STELLE);
    expect(gemerkteStelle()).toBe(STELLE);
  });

  it("liefert null, wenn nichts gemerkt ist", () => {
    expect(gemerkteStelle()).toBeNull();
  });

  it("vergisst sie nach Gebrauch", () => {
    merkeStelle(STELLE);
    vergissStelle();
    expect(gemerkteStelle()).toBeNull();
  });

  it("vergisst sie nach 24 Stunden", () => {
    // Sonst poppt eine drei Wochen alte Absicht nach dem nächsten Anmelden
    // wieder auf, und niemand weiss warum.
    vi.useFakeTimers();
    merkeStelle(STELLE);
    vi.advanceTimersByTime(24 * 60 * 60 * 1000 + 1);
    expect(gemerkteStelle()).toBeNull();
  });

  it("hält sie kurz vor Ablauf noch", () => {
    vi.useFakeTimers();
    merkeStelle(STELLE);
    vi.advanceTimersByTime(23 * 60 * 60 * 1000);
    expect(gemerkteStelle()).toBe(STELLE);
  });

  it("nimmt nur UUIDs an — beim Schreiben", () => {
    merkeStelle("/irgendwohin");
    merkeStelle("https://boese.example/phish");
    expect(gemerkteStelle()).toBeNull();
  });

  it("prüft auch beim LESEN, was im Speicher steht", () => {
    // localStorage ist von jedem Skript auf der Seite beschreibbar. Sich darauf
    // zu verlassen, dass nur wir hineinschreiben, wäre dieselbe Annahme, die
    // Open Redirect überhaupt erst möglich macht.
    window.localStorage.setItem(
      "wt.gemerkte-stelle",
      JSON.stringify({ jobId: "https://boese.example", gemerktAm: Date.now() })
    );
    expect(gemerkteStelle()).toBeNull();
  });

  it("überlebt kaputten Inhalt, ohne zu werfen", () => {
    window.localStorage.setItem("wt.gemerkte-stelle", "kein json {{{");
    expect(gemerkteStelle()).toBeNull();
  });

  it("kommt mit einem Speicher klar, der DA ist, aber nichts kann", () => {
    // Genau dieser Fall hat die CI rot gemacht und lokal nicht: Node 25 stellt
    // ein globales `localStorage` bereit, das ohne `--localstorage-file` keine
    // Methoden hat. Es wirft nicht — es kann nur nichts. Ein Schutz, der nur
    // Ausnahmen abfängt, greift dagegen nicht.
    vi.spyOn(window, "localStorage", "get").mockReturnValue({} as Storage);

    expect(() => merkeStelle(STELLE)).not.toThrow();
    expect(gemerkteStelle()).toBeNull();
  });

  it("wirft nicht, wenn der Speicher gesperrt ist", () => {
    // Safari im privaten Modus wirft beim Zugriff. Eine gemerkte Stelle ist
    // Komfort — sie darf die Seite nicht umwerfen.
    vi.spyOn(window, "localStorage", "get").mockImplementation(() => {
      throw new Error("gesperrt");
    });
    expect(() => merkeStelle(STELLE)).not.toThrow();
    expect(gemerkteStelle()).toBeNull();
  });
});
