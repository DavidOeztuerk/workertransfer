import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import type { PaletteMode } from "@mui/material";

const SPEICHER = "wt.colorMode";

/**
 * Was die BESUCHERIN eingestellt hat — nicht, was das System vorgibt.
 *
 * `system` ist ein eigener Wert und nicht "hell": nur so lässt sich später
 * unterscheiden, ob jemand hell GEWÄHLT hat oder ob wir seinem Gerät folgen.
 * Wer das zusammenlegt, kann die Wahl nie wieder zurücknehmen.
 */
export type ColorPreference = PaletteMode | "system";

interface PreferencesState {
  colorMode: ColorPreference;
}

/**
 * `localStorage` kann werfen — im privaten Fenster, bei blockierten
 * Seitendaten, in einer Vorschau. Ein Fehler beim LESEN einer Vorliebe darf die
 * Anwendung nicht am Start hindern.
 */
function gespeichert(): ColorPreference {
  try {
    const wert = localStorage.getItem(SPEICHER);
    return wert === "light" || wert === "dark" || wert === "system" ? wert : "system";
  } catch {
    return "system";
  }
}

const preferencesSlice = createSlice({
  name: "preferences",
  initialState: { colorMode: gespeichert() } as PreferencesState,
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
  },
});

export const { colorModeSet } = preferencesSlice.actions;
export default preferencesSlice.reducer;
