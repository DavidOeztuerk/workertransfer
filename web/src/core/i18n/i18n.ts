import i18next from "i18next";
import { initReactI18next } from "react-i18next";

import { resolveLanguage, type LanguagePreference } from "../store/preferencesSlice";
import de from "./catalogs/de";
import en from "./catalogs/en";
import fr from "./catalogs/fr";

/**
 * The catalogues, and who decides which one applies.
 *
 * <strong>German is the source</strong>, not merely one of three: the German
 * texts came first, are written and reviewed, and everything else is
 * translated from them. That is why German is also the fallback — a text in
 * the source language is better than a missing one.
 *
 * <strong>The fallback is switched on explicitly.</strong> Without it i18next
 * shows the key itself, and `settings.language.label` in the middle of a page
 * looks like a broken program rather than a missing translation. It should
 * still be visibly missing — the test that holds the catalogues against each
 * other takes care of that, not the UI.
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
    // React escapes by itself, and more thoroughly. Escaping twice turns an
    // apostrophe into an `&#39;` in the middle of a sentence.
    escapeValue: false,
  },
});

/** Sets the language from the preference. `system` follows the device (ADR-0031). */
export function applyLanguage(preference: LanguagePreference): void {
  const language = resolveLanguage(preference);

  if (i18n.language !== language) {
    void i18n.changeLanguage(language);
  }

  if (typeof document !== "undefined") {
    // Without this `<html lang>` stays German: screen readers then speak the
    // text with the wrong pronunciation, and the browser's hyphenation splits
    // words by the wrong rules.
    document.documentElement.lang = language;
  }
}
