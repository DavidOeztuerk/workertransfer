import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import type { PaletteMode } from "@mui/material";

const SPEICHER = "wt.colorMode";
const SPRACHSPEICHER = "wt.language";

/**
 * Was die BESUCHERIN eingestellt hat — nicht, was das System vorgibt.
 *
 * `system` ist ein eigener Wert und nicht "hell": nur so lässt sich später
 * unterscheiden, ob jemand hell GEWÄHLT hat oder ob wir seinem Gerät folgen.
 * Wer das zusammenlegt, kann die Wahl nie wieder zurücknehmen.
 */
export type ColorPreference = PaletteMode | "system";

/** Die Sprachen, die es gibt. Deutsch ist die Quelle, aus der übersetzt wurde. */
export const SPRACHEN = ["de", "en", "fr"] as const;

export type Sprache = (typeof SPRACHEN)[number];

/**
 * Was die BESUCHERIN gewählt hat — nicht, was ihr Gerät sagt.
 *
 * `system` ist ein eigener Wert, aus genau dem Grund, der oben beim Farbmodus
 * steht: wer es beim Start zu `de` zusammenfaltet, weil das Gerät gerade Deutsch
 * sagt, kann danach nie mehr unterscheiden, ob jemand Deutsch GEWÄHLT hat oder
 * ob wir nur folgen — und die Oberfläche folgt einem Wechsel der Systemsprache
 * nicht mehr (ADR-0031).
 */
export type Sprachvorliebe = Sprache | "system";

interface PreferencesState {
  colorMode: ColorPreference;
  language: Sprachvorliebe;
}

/**
 * `localStorage` kann werfen — im privaten Fenster, bei blockierten
 * Seitendaten, in einer Vorschau. Ein Fehler beim LESEN einer Vorliebe darf die
 * Anwendung nicht am Start hindern.
 */
function saved(): ColorPreference {
  try {
    const value = localStorage.getItem(SPEICHER);
    return value === "light" || value === "dark" || value === "system" ? value : "system";
  } catch {
    return "system";
  }
}

/** Wie <see cref="gespeichert"/>, und aus denselben Gründen fehlertolerant. */
function gespeicherteSprache(): Sprachvorliebe {
  try {
    const value = localStorage.getItem(SPRACHSPEICHER);
    return value === "system" || SPRACHEN.includes(value as Sprache)
      ? (value as Sprachvorliebe)
      : "system";
  } catch {
    return "system";
  }
}

/**
 * Was das Gerät sagt, auf eine Sprache abgebildet, die es hier gibt.
 *
 * `navigator.language` liefert `de-AT`, `fr-CA`, `en-GB` — die Region
 * interessiert hier nicht, nur die Sprache. Was nicht dabei ist, wird Deutsch:
 * das ist die Quellsprache, und ein Text in der Quelle ist besser als einer, der
 * gar nicht da ist.
 */
export function spracheDesGeraets(): Sprache {
  const raw = typeof navigator === "undefined" ? "" : (navigator.language ?? "");
  const nur = raw.toLowerCase().split("-")[0];

  return SPRACHEN.includes(nur as Sprache) ? (nur as Sprache) : "de";
}

/**
 * Hat dieses GERÄT schon einmal eine Sprache gewählt?
 *
 * Der Unterschied zu <c>gespeicherteSprache()</c> ist der ganze Punkt: die gibt
 * „system" zurück, wenn nichts gespeichert ist UND wenn jemand „Wie mein Gerät"
 * gewählt hat. Für die Frage „darf die Kontosprache das hier überschreiben?"
 * sind das zwei verschiedene Fälle, und nur `null` heisst „nie gewählt".
 */
export function hatEigeneSprachwahl(): boolean {
  try {
    return localStorage.getItem(SPRACHSPEICHER) !== null;
  } catch {
    // Nicht lesen zu können heisst nicht „nie gewählt": in dem Fall ist gar
    // nichts bekannt, und die Kontosprache ist die bessere Auskunft.
    return false;
  }
}

/** Welche Sprache wirklich gezeichnet wird. */
export function aufgeloest(vorliebe: Sprachvorliebe): Sprache {
  return vorliebe === "system" ? spracheDesGeraets() : vorliebe;
}

const preferencesSlice = createSlice({
  name: "preferences",
  initialState: {
    colorMode: saved(),
    language: gespeicherteSprache(),
  } as PreferencesState,
  reducers: {
    colorModeSet(state, action: PayloadAction<ColorPreference>) {
      state.colorMode = action.payload;
      try {
        localStorage.setItem(SPEICHER, action.payload);
      } catch {
        // Nicht schreiben zu können ist unangenehm, aber kein Grund, die
        // Umschaltung für diese Sitzung zu verweigern.
      }
    },

    languageSet(state, action: PayloadAction<Sprachvorliebe>) {
      state.language = action.payload;
      try {
        localStorage.setItem(SPRACHSPEICHER, action.payload);
      } catch {
        // Siehe oben.
      }
    },
  },
});

export const { colorModeSet, languageSet } = preferencesSlice.actions;
export default preferencesSlice.reducer;
