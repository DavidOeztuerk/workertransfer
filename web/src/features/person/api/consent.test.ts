import { describe, expect, it } from "vitest";

import de from "../../../core/i18n/kataloge/de";
import { parseCapability } from "./consent";

/**
 * Jede Fähigkeit, die das Backend vergibt, muss hier einen Namen bekommen.
 *
 * Der Anlass: `github.visibility:public` fehlte in der Tabelle, und die
 * Freigabenseite zeigte dafür die rohe Zeichenkette. Das ist kein Schönheits-
 * fehler — auf der Seite, auf der jemand seine Freigaben zurückzieht, ist ein
 * unlesbarer Eintrag genau der, den man stehen lässt.
 *
 * Die Liste steht ausgeschrieben da und wird nicht aus dem Backend abgeleitet:
 * eine neue Fähigkeit soll diesen Test rot machen, damit jemand ein Wort dafür
 * wählt, statt dass es beim ersten Anzeigen auffällt.
 *
 * Geprüft wird der SCHLÜSSEL und dass er im Katalog steht. Nur beides zusammen
 * trägt: i18next gibt einen unbekannten Schlüssel unverändert zurück, ein
 * Tippfehler stünde also roh auf genau der Seite, um die es hier geht.
 */
const ALLE_BEREICHE = [
  ["profile", "bereichProfile"],
  ["resume", "bereichResume"],
  ["portfolio", "bereichPortfolio"],
  ["market", "bereichMarket"],
  ["github", "bereichGithub"],
] as const;

describe("parseCapability", () => {
  it.each(ALLE_BEREICHE)("nennt für %s einen Katalogschlüssel", (roh, name) => {
    const schluessel = `freigaben.${name}`;
    expect(parseCapability(`${roh}.visibility:public`).area).toBe(schluessel);
    expect(de.freigaben).toHaveProperty(name);
  });

  it("nennt den Mandanten, wenn die Freigabe einem Unternehmen gilt", () => {
    const id = "7d9d3890-eccb-47f7-b793-514ce6283fdf";

    expect(parseCapability(`market.visibility:tenant:${id}`)).toEqual({
      area: "freigaben.bereichMarket",
      tenantId: id,
      public: false,
    });
  });

  it("erkennt eine Freigabe an alle", () => {
    expect(parseCapability("profile.visibility:public")).toEqual({
      area: "freigaben.bereichProfile",
      tenantId: null,
      public: true,
    });
  });

  /**
   * Eine unbekannte Form wird gezeigt, nicht verschluckt. Eine Freigabe zu
   * verbergen, weil ihr Format nicht erkannt wurde, wäre auf dieser Seite der
   * schlimmste denkbare Fehler — sie liesse sich dann auch nicht zurückziehen.
   */
  it("gibt bei unbekannter Form null zurück, statt zu raten", () => {
    expect(parseCapability("etwas.ganz.anderes").area).toBeNull();
  });
});
