import { createTheme, type PaletteMode, type ThemeOptions } from "@mui/material/styles";

import { accent, brand, neutral, semantic } from "./tokens/colors";
import { elevation, radius, spacingUnit } from "./tokens/spacing";
import { fontFamily, monoFamily, scale } from "./tokens/typography";

/**
 * Das Theme — gebaut aus den Tokens, nicht daneben.
 *
 * Alle Farbwerte kommen aus `tokens/colors`. Ein Literal in einer Komponente
 * ist deshalb immer ein Fehler und keine Ausnahme: die zweite Quelle ist genau
 * das, was beim nächsten Dunkelmodus auseinanderläuft.
 */
export const getThemeOptions = (mode: PaletteMode): ThemeOptions => {
  const light = mode === "light";

  return {
    palette: {
      mode,
      primary: {
        main: light ? brand[600] : brand[400],
        light: light ? brand[400] : brand[300],
        dark: light ? brand[800] : brand[600],
        contrastText: neutral[0],
      },
      secondary: {
        main: light ? accent[600] : accent[400],
        light: accent[400],
        dark: accent[800],
        contrastText: light ? neutral[0] : neutral[950],
      },
      success: { main: semantic.granted.main, light: semantic.granted.light, dark: semantic.granted.dark },
      error: { main: semantic.revoked.main, light: semantic.revoked.light, dark: semantic.revoked.dark },
      warning: { main: semantic.pending.main, light: semantic.pending.light, dark: semantic.pending.dark },
      info: { main: semantic.info.main, light: semantic.info.light, dark: semantic.info.dark },
      background: {
        default: light ? neutral[50] : neutral[950],
        paper: light ? neutral[0] : neutral[900],
      },
      text: {
        primary: light ? neutral[900] : neutral[100],
        secondary: light ? neutral[600] : neutral[400],
        disabled: light ? neutral[400] : neutral[600],
      },
      divider: light ? neutral[200] : neutral[800],
      action: {
        hover: light ? "rgb(107 94 242 / 0.05)" : "rgb(164 174 255 / 0.09)",
        selected: light ? "rgb(107 94 242 / 0.10)" : "rgb(164 174 255 / 0.16)",
        focus: light ? "rgb(107 94 242 / 0.14)" : "rgb(164 174 255 / 0.22)",
      },
    },

    shape: { borderRadius: radius.md },
    spacing: spacingUnit,

    typography: {
      fontFamily,
      ...scale,
      fontWeightMedium: 560,
      fontWeightBold: 680,
    },

    components: {
      // Der Fokusring ist Bedienbarkeit, nicht Zierde: wer mit der Tastatur
      // navigiert, muss jederzeit sehen, wo er steht. MUIs Vorgabe ist an
      // mehreren Stellen unsichtbar auf farbigem Grund.
      MuiCssBaseline: {
        styleOverrides: {
          ":root": { colorScheme: mode },
          "*:focus-visible": {
            outline: `2px solid ${light ? brand[600] : brand[300]}`,
            outlineOffset: 2,
            borderRadius: radius.sm,
          },
          body: { fontFamily, WebkitFontSmoothing: "antialiased" },
          code: { fontFamily: monoFamily },
          // Wer "weniger Bewegung" eingestellt hat, meint es.
          "@media (prefers-reduced-motion: reduce)": {
            "*": { animationDuration: "0.01ms !important", transitionDuration: "0.01ms !important" },
          },
        },
      },

      MuiButton: {
        defaultProps: { disableElevation: true },
        styleOverrides: {
          root: { borderRadius: radius.sm, paddingInline: 16, minHeight: 40 },
          sizeSmall: { minHeight: 32, paddingInline: 12 },
          sizeLarge: { minHeight: 48, paddingInline: 22, fontSize: "1rem" },
        },
      },

      MuiPaper: {
        defaultProps: { elevation: 0 },
        styleOverrides: {
          root: { backgroundImage: "none" },
          outlined: { borderColor: light ? neutral[200] : neutral[800] },
        },
      },

      MuiCard: {
        defaultProps: { variant: "outlined" },
        styleOverrides: {
          root: { borderRadius: radius.lg, boxShadow: light ? elevation.raised : "none" },
        },
      },

      MuiTextField: { defaultProps: { size: "small", fullWidth: true } },

      MuiOutlinedInput: {
        styleOverrides: {
          root: { borderRadius: radius.sm, backgroundColor: light ? neutral[0] : neutral[950] },
        },
      },

      // Ein Hinweis ohne Rand verschwimmt mit der Seite; gerade Fehler müssen
      // als eigener Block lesbar sein.
      MuiAlert: {
        defaultProps: { variant: "outlined" },
        styleOverrides: { root: { borderRadius: radius.md, alignItems: "flex-start" } },
      },

      MuiChip: {
        styleOverrides: {
          root: { borderRadius: radius.pill, fontWeight: 560 },
          sizeSmall: { height: 24 },
        },
      },

      MuiTooltip: {
        defaultProps: { arrow: true },
        styleOverrides: { tooltip: { borderRadius: radius.sm, fontSize: "0.8125rem" } },
      },

      MuiLink: {
        defaultProps: { underline: "hover" },
        styleOverrides: { root: { fontWeight: 560 } },
      },

      MuiDialog: {
        styleOverrides: { paper: { borderRadius: radius.xl, boxShadow: elevation.modal } },
      },
    },
  };
};

/** Fertig gebaut, für die Stellen, die kein `mode` von aussen bekommen. */
export const buildTheme = (mode: PaletteMode) => createTheme(getThemeOptions(mode));
