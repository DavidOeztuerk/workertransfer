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
 * Der Anlass ist gemessen: am 10.09.2026 kam die Adresse `bus` durch das
 * Formular, weil `noValidate` die Browserprüfung abschaltet und keine eigene
 * danebenstand. Der Server legte ein Konto an, die Mail scheiterte im
 * Hintergrund, und zurück blieb ein Konto auf `pending`, das niemand je
 * bestätigen kann.
 */
describe("die Formprüfung des Anmeldeformulars", () => {
  it("erkennt den Fall, der wirklich passiert ist", () => {
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
    // Grosszügig ist Absicht: diese Prüfung darf niemanden aussperren, dessen
    // Adresse ungewöhnlich aussieht und trotzdem gilt. Die Absage spricht der
    // Server.
    expect(siehtNachAdresseAus(adresse)).toBe(true);
  });

  it("zeigt bei einem leeren Feld noch keinen Fehler", () => {
    // Wer gerade erst anfängt zu tippen, bekäme sonst Rot, bevor er etwas
    // falsch gemacht hat.
    expect(emailFehler("")).toBeNull();
    expect(emailFehler("   ")).toBeNull();
    expect(passwortFehler("")).toBeNull();
  });

  it("misst das Passwort in Bytes, weil bcrypt Bytes zählt", () => {
    // „ä" ist zwei Bytes. Wer ein langes Passwort mit Umlauten wählt, liefe
    // sonst in eine Absage, die er sich nicht erklären kann.
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
