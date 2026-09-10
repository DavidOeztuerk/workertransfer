import { describe, expect, it } from "vitest";

import de from "../../core/i18n/kataloge/de";
import {
  BERUFSFELDER,
  belegartenFuer,
  hochladbareBelege,
  leseBerufsfeld,
  zeigtGitHub,
} from "./berufsfelder";

/** Ein Katalogschlüssel wie `beleg.meisterbrief`, aufgelöst am deutschen Katalog. */
function katalogHat(schluessel: string): boolean {
  return schluessel
    .split(".")
    .reduce<unknown>(
      (stand, teil) =>
        typeof stand === "object" && stand !== null
          ? (stand as Record<string, unknown>)[teil]
          : undefined,
      de,
    ) !== undefined;
}

describe("Berufsfelder und ihre Belege (ADR-0039)", () => {
  it("führt genau die elf Etiketten des Servers", () => {
    // Eine Menge, kein Enthaltensein: ein zwölftes Feld soll auffallen.
    expect([...BERUFSFELDER]).toEqual([
      "handwerk",
      "industrie_technik",
      "bau",
      "gesundheit_pflege",
      "logistik_verkehr",
      "gastronomie_hotel",
      "handel_verkauf",
      "buero_verwaltung",
      "it_software",
      "bildung_soziales",
      "sonstiges",
    ]);
  });

  it("liest ein unbekanntes Etikett als „keins“ statt zu scheitern", () => {
    expect(leseBerufsfeld("metallbauer")).toBeNull();
    expect(leseBerufsfeld(null)).toBeNull();
    expect(leseBerufsfeld("handwerk")).toBe("handwerk");
  });

  it("bietet GitHub bei it_software und ohne Feld an, sonst nicht", () => {
    expect(zeigtGitHub(null)).toBe(true);
    expect(zeigtGitHub("it_software")).toBe(true);
    expect(zeigtGitHub("handwerk")).toBe(false);
    expect(zeigtGitHub("gesundheit_pflege")).toBe(false);
  });

  it("gibt jedem Feld die allgemeinen Belege mit", () => {
    // Die feldeigenen Belege kommen dazu, sie ersetzen nichts.
    for (const feld of BERUFSFELDER) {
      const schluessel = belegartenFuer(feld).map((art) => art.schluessel);

      expect(schluessel, feld).toContain("beleg.arbeitszeugnis");
      expect(schluessel, feld).toContain("beleg.zertifikat");
      expect(schluessel, feld).toContain("beleg.referenz");
    }
  });

  it("nennt jeden Beleg nur einmal", () => {
    for (const feld of BERUFSFELDER) {
      const schluessel = belegartenFuer(feld).map((art) => art.schluessel);

      expect(new Set(schluessel).size, feld).toBe(schluessel.length);
    }
  });

  it("bietet ohne Berufsfeld nur die allgemeinen Belege an", () => {
    expect(belegartenFuer(null).map((art) => art.schluessel)).toEqual([
      "beleg.arbeitszeugnis",
      "beleg.zertifikat",
      "beleg.referenz",
    ]);
  });

  it("lässt Führungs- und Gesundheitszeugnis nennen, nie hochladen", () => {
    // Nennbar, weil Arbeitgeber danach fragen — und nie hochladbar.
    expect(
      belegartenFuer("gesundheit_pflege").map((art) => art.schluessel),
    ).toContain("beleg.fuehrungszeugnis");
    expect(
      hochladbareBelege("gesundheit_pflege").map((art) => art.schluessel),
    ).not.toContain("beleg.fuehrungszeugnis");

    expect(
      belegartenFuer("gastronomie_hotel").map((art) => art.schluessel),
    ).toContain("beleg.gesundheitszeugnis");
    expect(
      hochladbareBelege("gastronomie_hotel").map((art) => art.schluessel),
    ).not.toContain("beleg.gesundheitszeugnis");
  });

  it("hat für jeden Beleg einen Text im Katalog", () => {
    // Sonst erschiene bei einem neuen Beleg der Schlüssel auf der Seite.
    for (const feld of [...BERUFSFELDER, null]) {
      for (const art of belegartenFuer(feld)) {
        expect(katalogHat(art.schluessel), art.schluessel).toBe(true);
      }
    }
  });

  it("hat für jedes Berufsfeld einen Namen im Katalog", () => {
    for (const feld of BERUFSFELDER) {
      expect(katalogHat(`beruf.feld.${feld}`), feld).toBe(true);
    }
  });
});
