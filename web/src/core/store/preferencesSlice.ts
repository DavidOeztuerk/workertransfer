import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import type { PaletteMode } from "@mui/material";

// Stored in real browsers: renaming either string loses a visitor's choice.
const COLOR_MODE_STORAGE_KEY = "wt.colorMode";
const LANGUAGE_STORAGE_KEY = "wt.language";

/**
 * What the VISITOR set — not what the system prescribes.
 *
 * `system` is a value of its own and not "light": only that way can we later
 * tell whether somebody CHOSE light or whether we follow their device. Whoever
 * merges the two can never take the choice back again.
 */
export type ColorPreference = PaletteMode | "system";

/** The languages there are. German is the source everything was translated from. */
export const LANGUAGES = ["de", "en", "fr"] as const;

export type Language = (typeof LANGUAGES)[number];

/**
 * What the VISITOR chose — not what their device says.
 *
 * `system` is a value of its own, for exactly the reason given above for the
 * colour mode: whoever folds it into `de` at startup because the device happens
 * to say German can never again tell whether somebody CHOSE German or whether
 * we only follow — and the UI no longer follows a change of the system
 * language (ADR-0031).
 */
export type LanguagePreference = Language | "system";

interface PreferencesState {
  colorMode: ColorPreference;
  language: LanguagePreference;
}

/**
 * `localStorage` can throw — in a private window, with blocked site data, in a
 * preview. An error while READING a preference must not stop the application
 * from starting.
 */
function savedColorMode(): ColorPreference {
  try {
    const value = localStorage.getItem(COLOR_MODE_STORAGE_KEY);
    return value === "light" || value === "dark" || value === "system" ? value : "system";
  } catch {
    return "system";
  }
}

/** Like `savedColorMode()`, and fault-tolerant for the same reasons. */
function savedLanguage(): LanguagePreference {
  try {
    const value = localStorage.getItem(LANGUAGE_STORAGE_KEY);
    return value === "system" || LANGUAGES.includes(value as Language)
      ? (value as LanguagePreference)
      : "system";
  } catch {
    return "system";
  }
}

/**
 * What the device says, mapped onto a language that exists here.
 *
 * `navigator.language` yields `de-AT`, `fr-CA`, `en-GB` — the region does not
 * matter here, only the language. Whatever is not among them becomes German:
 * that is the source language, and a text in the source is better than one
 * that is not there at all.
 */
export function deviceLanguage(): Language {
  const raw = typeof navigator === "undefined" ? "" : (navigator.language ?? "");
  const bare = raw.toLowerCase().split("-")[0];

  return LANGUAGES.includes(bare as Language) ? (bare as Language) : "de";
}

/**
 * Has this DEVICE ever chosen a language?
 *
 * The difference to `savedLanguage()` is the whole point: that one returns
 * "system" when nothing is stored AND when somebody chose "like my device".
 * For the question "may the account language override this?" those are two
 * different cases, and only `null` means "never chosen".
 */
export function hasOwnLanguageChoice(): boolean {
  try {
    return localStorage.getItem(LANGUAGE_STORAGE_KEY) !== null;
  } catch {
    // Not being able to read does not mean "never chosen": in that case
    // nothing is known at all, and the account language is the better answer.
    return false;
  }
}

/** Which language is actually drawn. */
export function resolveLanguage(preference: LanguagePreference): Language {
  return preference === "system" ? deviceLanguage() : preference;
}

const preferencesSlice = createSlice({
  name: "preferences",
  initialState: {
    colorMode: savedColorMode(),
    language: savedLanguage(),
  } as PreferencesState,
  reducers: {
    colorModeSet(state, action: PayloadAction<ColorPreference>) {
      state.colorMode = action.payload;
      try {
        localStorage.setItem(COLOR_MODE_STORAGE_KEY, action.payload);
      } catch {
        // Not being able to write is unpleasant, but no reason to refuse the
        // switch for this session.
      }
    },

    languageSet(state, action: PayloadAction<LanguagePreference>) {
      state.language = action.payload;
      try {
        localStorage.setItem(LANGUAGE_STORAGE_KEY, action.payload);
      } catch {
        // See above.
      }
    },
  },
});

export const { colorModeSet, languageSet } = preferencesSlice.actions;
export default preferencesSlice.reducer;
