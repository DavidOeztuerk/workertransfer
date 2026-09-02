import { useMemo } from "react";
import useMediaQuery from "@mui/material/useMediaQuery";
import { createTheme, responsiveFontSizes, type PaletteMode, type Theme } from "@mui/material/styles";

import { colorModeSet, type ColorPreference } from "../../core/store/preferencesSlice";
import { useAppDispatch, useAppSelector } from "../../core/store/hooks";
import { getThemeOptions } from "../../styles/theme";

interface ThemeModeReturn {
  theme: Theme;
  /** Was tatsächlich gezeichnet wird. */
  mode: PaletteMode;
  /** Was die Person GEWÄHLT hat — inklusive "system". */
  preference: ColorPreference;
  toggle: () => void;
  setPreference: (next: ColorPreference) => void;
}

/**
 * Das Theme und seine Umschaltung.
 *
 * <strong>Heisst `useThemeMode` und nicht `useTheme`</strong>, weil MUI selbst
 * ein `useTheme()` mitbringt, das das Theme-OBJEKT liefert. Zwei Hooks gleichen
 * Namens in einer Datei sind der Import, den man beim Überfliegen verwechselt.
 *
 * Die Vorliebe kommt aus dem Store, nicht aus lokalem Zustand: sie wird an
 * mehreren Stellen gelesen (Kopfzeile, Einstellungen), und zwei Quellen für
 * dieselbe Wahl gehen beim ersten Umschalten auseinander.
 *
 * `system` bleibt ein eigener Wert. Wer es beim Start zu "hell" zusammenfaltet,
 * kann nie wieder unterscheiden, ob jemand hell GEWÄHLT hat oder ob wir dem
 * Gerät folgen — und dann folgt die Oberfläche einem Wechsel der Systemvorliebe
 * nicht mehr.
 */
export function useThemeMode(): ThemeModeReturn {
  const dispatch = useAppDispatch();
  const preference = useAppSelector((state) => state.preferences.colorMode);
  const systemDark = useMediaQuery("(prefers-color-scheme: dark)");

  const mode: PaletteMode =
    preference === "system" ? (systemDark ? "dark" : "light") : preference;

  const theme = useMemo(() => responsiveFontSizes(createTheme(getThemeOptions(mode))), [mode]);

  return {
    theme,
    mode,
    preference,
    toggle: () => dispatch(colorModeSet(mode === "light" ? "dark" : "light")),
    setPreference: (next) => dispatch(colorModeSet(next)),
  };
}
