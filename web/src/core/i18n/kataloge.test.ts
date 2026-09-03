import { describe, expect, it } from "vitest";

import { SPRACHEN } from "../store/preferencesSlice";
import de from "./kataloge/de";
import en from "./kataloge/en";
import fr from "./kataloge/fr";

/** Jeden Schlüssel als flachen Pfad, damit ein Vergleich etwas sagt. */
function pfade(wert: unknown, praefix = ""): string[] {
  if (typeof wert !== "object" || wert === null) {
    return [praefix];
  }

  return Object.entries(wert).flatMap(([schluessel, unten]) =>
    pfade(unten, praefix === "" ? schluessel : `${praefix}.${schluessel}`)
  );
}

const kataloge = { de, en, fr };

describe("die Kataloge", () => {
  // Der Typ hält die Schlüssel schon fest — aber nur beim Übersetzen. Wer die
  // Kataloge einmal als JSON lädt oder den Typ lockert, verliert das
  // wortlos. Dieser Test hält es unabhängig davon.
  it("haben in jeder Sprache dieselben Schlüssel", () => {
    const quelle = pfade(de).sort();

    for (const sprache of SPRACHEN) {
      expect(pfade(kataloge[sprache]).sort(), `Katalog ${sprache}`).toEqual(quelle);
    }
  });

  // Eine kopierte Zeile, die niemand übersetzt hat, sieht in der Oberfläche aus
  // wie eine fehlende Übersetzung — nur fällt sie nirgends auf.
  //
  // Die Ausnahmen stehen je SPRACHE, nicht je Schlüssel: „Link" heisst auf
  // Englisch gleich, auf Französisch „Lien" — eine Ausnahme für den Schlüssel
  // liesse die französische Zeile stillschweigend mit durch.
  it("übersetzen wirklich, statt Deutsch zu kopieren", () => {
    const erlaubt = new Set([
      // Sprachnamen stehen in ihrer eigenen Sprache, in jedem Katalog.
      "en:sprache.de",
      "en:sprache.en",
      "en:sprache.fr",
      "fr:sprache.de",
      "fr:sprache.en",
      "fr:sprache.fr",
      // Der deutsche Wortlaut IST hier englisch — ein Schlagwort, kein Versehen.
      "en:start.grundlage3Titel",
      // Gleiches Wort in beiden Sprachen. Jede Zeile ist ein Einzelfall und
      // kein Freibrief — Französisch steht auf keiner davon.
      "en:arbeiten.feldLink",
      "en:markt.status",
      "en:freigaben.bereichGithub",
      "en:stelle.remoteHybrid",
      "en:stellen.website",
      "en:firmenprofil.website",
      "en:gespraeche.start",
      "en:firmentransfers.start",
      "en:firmentransfers.titel",
      "en:mannschaft.rolleAdmin",
      "en:kopf.github",
      "en:kopf.team",
      "fr:freigaben.bereichProfile",
      "fr:freigaben.bereichGithub",
      "fr:bewerbungen.teilProfil",
      "fr:kopf.profil",
      "fr:kopf.github",
      // Produktnamen, in jeder Sprache dieselben.
      "en:kandidaten.faehigkeitenBeispiel",
      "fr:kandidaten.faehigkeitenBeispiel",
    ]);

    for (const sprache of ["en", "fr"] as const) {
      const gleich = pfade(de).filter(
        (pfad) =>
          !erlaubt.has(`${sprache}:${pfad}`) &&
          lies(de, pfad) === lies(kataloge[sprache], pfad)
      );

      expect(gleich, `unübersetzt in ${sprache}`).toEqual([]);
    }
  });

  it("lassen keinen Text leer", () => {
    for (const sprache of SPRACHEN) {
      for (const pfad of pfade(kataloge[sprache])) {
        expect(lies(kataloge[sprache], pfad).trim(), `${sprache}: ${pfad}`).not.toBe("");
      }
    }
  });
});

function lies(katalog: unknown, pfad: string): string {
  return pfad.split(".").reduce<unknown>(
    (wert, teil) => (wert as Record<string, unknown>)[teil],
    katalog
  ) as string;
}
