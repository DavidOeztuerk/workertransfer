import { describe, expect, it } from "vitest";

import { SPRACHEN } from "../store/preferencesSlice";
import de from "./kataloge/de";
import en from "./kataloge/en";
import fr from "./kataloge/fr";

/** Jeden Schlüssel als flachen Pfad, damit ein Vergleich etwas sagt. */
function pfade(value: unknown, praefix = ""): string[] {
  if (typeof value !== "object" || value === null) {
    return [praefix];
  }

  return Object.entries(value).flatMap(([key, unten]) =>
    pfade(unten, praefix === "" ? key : `${praefix}.${key}`)
  );
}

const kataloge = { de, en, fr };

describe("die Kataloge", () => {
  // Der Typ hält die Schlüssel schon fest — aber nur beim Übersetzen. Wer die
  // Kataloge einmal als JSON lädt oder den Typ lockert, verliert das
  // wortlos. Dieser Test hält es unabhängig davon.
  it("haben in jeder Sprache dieselben Schlüssel", () => {
    const quelle = pfade(de).sort();

    for (const language of SPRACHEN) {
      expect(pfade(kataloge[language]).sort(), `Katalog ${language}`).toEqual(quelle);
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
      "en:stellen.faehigkeitenBeispiel",
      "fr:stellen.faehigkeitenBeispiel",
      // Ein Copyright-Vermerk ist in jeder Sprache derselbe.
      "en:fuss.rechte",
      "fr:fuss.rechte",
      "fr:kandidaten.faehigkeitenBeispiel",
    ]);

    for (const language of ["en", "fr"] as const) {
      const gleich = pfade(de).filter(
        (path) =>
          !erlaubt.has(`${language}:${path}`) &&
          lies(de, path) === lies(kataloge[language], path)
      );

      expect(gleich, `unübersetzt in ${language}`).toEqual([]);
    }
  });

  it("lassen keinen Text leer", () => {
    for (const language of SPRACHEN) {
      for (const path of pfade(kataloge[language])) {
        expect(lies(kataloge[language], path).trim(), `${language}: ${path}`).not.toBe("");
      }
    }
  });
});

function lies(katalog: unknown, path: string): string {
  return path.split(".").reduce<unknown>(
    (value, teil) => (value as Record<string, unknown>)[teil],
    katalog
  ) as string;
}
