import i18next from "i18next";
import { initReactI18next } from "react-i18next";

import { aufgeloest, type Sprachvorliebe } from "../store/preferencesSlice";
import de from "./kataloge/de";
import en from "./kataloge/en";
import fr from "./kataloge/fr";

/**
 * Die Kataloge, und wer entscheidet, welcher gilt.
 *
 * <strong>Deutsch ist die Quelle</strong>, nicht nur eine von dreien: die
 * deutschen Texte waren zuerst da, sind geschrieben und geprüft, und alles
 * andere ist daraus übersetzt. Deshalb ist Deutsch auch der Rückfall — ein Text
 * in der Quellsprache ist besser als ein fehlender.
 *
 * <strong>Der Rückfall ist ausdrücklich eingeschaltet.</strong> Ohne ihn zeigt
 * i18next den Schlüssel selbst an, und `settings.language.label` mitten auf
 * einer Seite sieht aus wie ein kaputtes Programm statt wie eine fehlende
 * Übersetzung. Sichtbar fehlen soll sie trotzdem — dafür sorgt der Test, der
 * die Kataloge gegeneinander hält, nicht die Oberfläche.
 */
export const i18n = i18next.createInstance();

void i18n.use(initReactI18next).init({
  resources: {
    de: { translation: de },
    en: { translation: en },
    fr: { translation: fr },
  },
  lng: "de",
  fallbackLng: "de",
  interpolation: {
    // React entkommt selbst, und zwar gründlicher. Doppelt entkommen macht aus
    // einem Apostroph ein `&#39;` mitten im Satz.
    escapeValue: false,
  },
});

/** Setzt die Sprache aus der Vorliebe. `system` folgt dem Gerät (ADR-0031). */
export function spracheAnwenden(vorliebe: Sprachvorliebe): void {
  const sprache = aufgeloest(vorliebe);

  if (i18n.language !== sprache) {
    void i18n.changeLanguage(sprache);
  }

  if (typeof document !== "undefined") {
    // Ohne das bleibt `<html lang>` auf Deutsch stehen: Vorleseprogramme
    // sprechen den Text dann mit der falschen Aussprache, und die Silbentrennung
    // des Browsers trennt nach den falschen Regeln.
    document.documentElement.lang = sprache;
  }
}
