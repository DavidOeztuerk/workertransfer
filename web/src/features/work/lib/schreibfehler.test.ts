import { describe, expect, it } from "vitest";

import { leerBriefHinweisSchluessel, schreibgrund, schreibschluessel } from "./schreibfehler";

describe("schreibgrund", () => {
  it("erkennt, dass kein Anbieter eingerichtet ist", () => {
    expect(schreibgrund("Es ist kein Entwurfsanbieter eingerichtet.")).toBe("noProvider");
    expect(schreibgrund("")).toBe("noProvider");
    expect(schreibgrund(null)).toBe("noProvider");
  });

  it("erkennt eine Zeitüberschreitung ohne den Rohsatz", () => {
    expect(schreibgrund("Der Entwurfsanbieter antwortet nicht (Zeitüberschreitung).")).toBe(
      "timeout",
    );
    expect(schreibschluessel("Zeitueberschreitung")).toBe("drafts.timeout");
  });

  it("verschluckt HTTP-Statuscodes — 402, 404, 422 sehen nicht wie der Fehler der Seite aus", () => {
    expect(schreibgrund("Der Entwurfsanbieter antwortet mit 404.")).toBe("providerRejected");
    expect(schreibgrund("Der Entwurfsanbieter antwortet mit 402.")).toBe("providerRejected");
    expect(schreibgrund("Der Entwurfsanbieter antwortet mit 422.")).toBe("providerRejected");
    expect(schreibschluessel("antwortet mit 404")).not.toContain("404");
  });

  it("ordnet einen verbotenen Schritt nicht dem Anbieter zu", () => {
    expect(
      schreibgrund("Ein Entwurf im Stand Fehlgeschlagen kann nicht geschrieben werden."),
    ).toBe("stepNotNow");
  });

  it("wählt den leeren Hinweis nach dem Grund, nicht immer „kein Anbieter“", () => {
    expect(leerBriefHinweisSchluessel("Es ist kein Entwurfsanbieter eingerichtet.")).toBe(
      "drafts.emptyLetterHint",
    );
    expect(leerBriefHinweisSchluessel("Zeitüberschreitung")).toBe("drafts.timeout");
    expect(leerBriefHinweisSchluessel("antwortet mit 404")).toBe("drafts.providerRejected");
  });
});
