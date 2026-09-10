import { describe, expect, it } from "vitest";

import {
  PASSWORT_HOECHSTBYTES,
  PASSWORT_MINDESTLAENGE,
  emailFehler,
  passwortBytes,
  passwortFehler,
  siehtNachAdresseAus,
} from "./formpruefung";

/**
 * Das Formular trägt `noValidate`, also blockt `type="email"` nichts. Ohne
 * eigene Prüfung kommen Zeichenketten ohne `@` durch.
 */
describe("die Formprüfung des Anmeldeformulars", () => {
  it("weist eine Zeichenkette ohne @ ab", () => {
    expect(siehtNachAdresseAus("bus")).toBe(false);
    expect(emailFehler("bus")).toBe("registrierung.emailUngueltig");
  });

  it.each([
    ["", "leer"],
    ["@example.org", "nichts davor"],
    ["anna@", "nichts dahinter"],
    ["anna@localhost", "kein Punkt in der Domain"],
    ["anna@example.", "Punkt am Rand"],
    ["anna example.org", "Leerzeichen"],
    ["a@b@c.de", "zwei Klammeraffen"],
  ])("weist %s ab (%s)", (adresse) => {
    expect(siehtNachAdresseAus(adresse)).toBe(false);
  });

  it.each([
    "anna@example.org",
    "anna.beispiel@firma.co.uk",
    "anna+bewerbung@example.org",
    "a@b.de",
    "jörg@example.org",
  ])("lässt %s durch", (adresse) => {
    // Grosszügig: die Absage spricht der Server.
    expect(siehtNachAdresseAus(adresse)).toBe(true);
  });

  it("zeigt bei einem leeren Feld noch keinen Fehler", () => {
    expect(emailFehler("")).toBeNull();
    expect(emailFehler("   ")).toBeNull();
    expect(passwortFehler("")).toBeNull();
  });

  it("misst das Passwort in Bytes, weil bcrypt Bytes zählt", () => {
    // „ä" ist zwei Bytes.
    expect(passwortBytes("ä")).toBe(2);

    const knapp = "ä".repeat(PASSWORT_HOECHSTBYTES / 2);
    expect(passwortFehler(knapp)).toBeNull();
    expect(passwortFehler(`${knapp}a`)).toBe("registrierung.passwortZuLang");
  });

  it("nennt die Mindestlänge, bevor jemand abschickt", () => {
    expect(passwortFehler("a".repeat(PASSWORT_MINDESTLAENGE - 1))).toBe(
      "registrierung.passwortZuKurz",
    );
    expect(passwortFehler("a".repeat(PASSWORT_MINDESTLAENGE))).toBeNull();
  });
});
