import { useMemo } from "react";
import useMediaQuery from "@mui/material/useMediaQuery";
import { createTheme, responsiveFontSizes, type PaletteMode, type Theme } from "@mui/material/styles";

import { colorModeSet, type ColorPreference } from "../../core/store/preferencesSlice";
import { useAppDispatch, useAppSelector } from "../../core/store/hooks";
import { getThemeOptions } from "../../styles/theme";

interface ThemeModeReturn {
  theme: Theme;
  /** What is actually drawn. */
  mode: PaletteMode;
  /** What the person CHOSE — "system" included. */
  preference: ColorPreference;
  toggle: () => void;
  setPreference: (next: ColorPreference) => void;
}

/**
 * The theme and its switch.
 *
 * <strong>Called `useThemeMode` and not `useTheme`</strong>, because MUI itself
 * brings a `useTheme()` that yields the theme OBJECT. Two hooks of the same name
 * in one file are the import one mixes up while skimming.
 *
 * The preference comes from the store, not from local state: it is read in
 * several places (header, settings), and two sources for the same choice drift
 * apart at the first switch.
 *
 * `system` stays a value of its own. Whoever folds it into "light" at start can
 * never again tell whether somebody CHOSE light or whether we follow the
 * device — and then the UI no longer follows a change of the system
 * preference.
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
