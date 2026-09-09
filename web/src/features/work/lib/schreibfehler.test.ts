import { describe, expect, it } from "vitest";

import { leerBriefHinweisSchluessel, schreibgrund, schreibschluessel } from "./schreibfehler";

describe("schreibgrund", () => {
  it("erkennt, dass kein Anbieter eingerichtet ist", () => {
    expect(schreibgrund("Es ist kein Entwurfsanbieter eingerichtet.")).toBe("keinAnbieter");
    expect(schreibgrund("")).toBe("keinAnbieter");
    expect(schreibgrund(null)).toBe("keinAnbieter");
  });

  it("erkennt eine Zeitüberschreitung ohne den Rohsatz", () => {
    expect(schreibgrund("Der Entwurfsanbieter antwortet nicht (Zeitüberschreitung).")).toBe(
      "zeitueberschreitung",
    );
    expect(schreibschluessel("Zeitueberschreitung")).toBe("entwurfs.zeitueberschreitung");
  });

  it("verschluckt HTTP-Statuscodes — 402, 404, 422 sehen nicht wie der Fehler der Seite aus", () => {
    expect(schreibgrund("Der Entwurfsanbieter antwortet mit 404.")).toBe("anbieterAblehnung");
    expect(schreibgrund("Der Entwurfsanbieter antwortet mit 402.")).toBe("anbieterAblehnung");
    expect(schreibgrund("Der Entwurfsanbieter antwortet mit 422.")).toBe("anbieterAblehnung");
    expect(schreibschluessel("antwortet mit 404")).not.toContain("404");
  });

  it("ordnet einen verbotenen Schritt nicht dem Anbieter zu", () => {
    expect(
      schreibgrund("Ein Entwurf im Stand Fehlgeschlagen kann nicht geschrieben werden."),
    ).toBe("schrittJetztNicht");
  });

  it("wählt den leeren Hinweis nach dem Grund, nicht immer „kein Anbieter“", () => {
    expect(leerBriefHinweisSchluessel("Es ist kein Entwurfsanbieter eingerichtet.")).toBe(
      "entwurfs.leerBriefHinweis",
    );
    expect(leerBriefHinweisSchluessel("Zeitüberschreitung")).toBe("entwurfs.zeitueberschreitung");
    expect(leerBriefHinweisSchluessel("antwortet mit 404")).toBe("entwurfs.anbieterAblehnung");
  });
});
