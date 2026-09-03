import { createTheme, type PaletteMode, type ThemeOptions } from "@mui/material/styles";

import {
  accent,
  borders,
  brand,
  focusRing,
  neutral,
  semantic,
  surfaces,
} from "./tokens/colors";
import { elevation, layer, motion, radius, size, spacingUnit } from "./tokens/spacing";
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

  // Die drei Leitern, aus denen fast jede Regel unten liest. Sie stehen hier
  // einmal, damit keine Komponente sich ihre eigene Ebene ausdenkt.
  const flaeche = light ? surfaces.light : surfaces.dark;
  const rand = light ? borders.light : borders.dark;
  const hoehe = light ? elevation.light : elevation.dark;

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
        default: flaeche.canvas,
        paper: flaeche.surface,
      },
      text: {
        primary: light ? neutral[900] : neutral[100],
        secondary: light ? neutral[600] : neutral[400],
        disabled: light ? neutral[400] : neutral[600],
      },
      divider: rand.default,
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
      MuiCssBaseline: {
        styleOverrides: {
          ":root": { colorScheme: mode },

          // Der Fokusring ist Bedienbarkeit, nicht Zierde: wer mit der Tastatur
          // navigiert, muss jederzeit sehen, wo er steht. MUIs Vorgabe ist an
          // mehreren Stellen unsichtbar auf farbigem Grund. Zwei Lagen, damit er
          // auf hellem wie auf dunklem Grund trägt.
          //
          // NICHT auf Formularfelder. Ein `input`, `select` oder `textarea`
          // steckt in einem `OutlinedInput`, und der zeichnet im Fokus schon
          // einen 2px-Rahmen um sich. Beides zusammen ergab zwei Rahmen
          // ineinander — einen am Feld, einen am Kasten darum —, und weil der
          // äussere rund und der innere eckig ist, sah es schief aus. Gemessen
          // an der Sprachwahl und den Filtern.
          "*:focus-visible:not(input):not(select):not(textarea)": {
            outline: "none",
            boxShadow: light ? focusRing.light : focusRing.dark,
            borderRadius: radius.sm,
          },

          // Das Feld selbst bekommt gar keinen eigenen Ring: der Kasten um es
          // herum ist der Fokus, und er ist gross genug, um gesehen zu werden.
          "input:focus-visible, select:focus-visible, textarea:focus-visible": {
            outline: "none",
          },

          body: {
            fontFamily,
            WebkitFontSmoothing: "antialiased",
            MozOsxFontSmoothing: "grayscale",
            textRendering: "optimizeLegibility",
          },
          code: { fontFamily: monoFamily },

          // Eine schmale Bildlaufleiste in den Farben der Oberfläche. Die
          // Vorgabe des Browsers ist im Dunkelmodus hell und schneidet einen
          // weissen Streifen in die Seite.
          "*::-webkit-scrollbar": { width: 10, height: 10 },
          "*::-webkit-scrollbar-thumb": {
            backgroundColor: rand.strong,
            borderRadius: radius.pill,
            border: `2px solid ${flaeche.canvas}`,
          },
          "*::-webkit-scrollbar-track": { background: "transparent" },

          // Wer "weniger Bewegung" eingestellt hat, meint es.
          "@media (prefers-reduced-motion: reduce)": {
            "*": {
              animationDuration: "0.01ms !important",
              transitionDuration: "0.01ms !important",
              scrollBehavior: "auto !important",
            },
          },
        },
      },

      MuiButton: {
        defaultProps: { disableElevation: true },
        styleOverrides: {
          root: {
            borderRadius: radius.sm,
            paddingInline: 18,
            minHeight: 40,
            transition: `background-color ${motion.fast}, box-shadow ${motion.fast}, transform ${motion.fast}`,
            // Ein Druckpunkt. Winzig, aber er ist der Unterschied zwischen
            // „angeklickt" und „vielleicht angeklickt".
            "&:active": { transform: "translateY(0.5px)" },
          },
          sizeSmall: { minHeight: 34, paddingInline: 14 },
          sizeLarge: { minHeight: 48, paddingInline: 26, fontSize: "1rem" },
          contained: {
            boxShadow: "none",
            "&:hover": { boxShadow: hoehe.hover },
          },
          outlined: {
            borderColor: rand.strong,
            "&:hover": { borderColor: light ? brand[400] : brand[500] },
          },
          text: { paddingInline: 12 },
        },
      },

      MuiIconButton: {
        styleOverrides: {
          root: {
            borderRadius: radius.sm,
            transition: `background-color ${motion.fast}`,
          },
        },
      },

      MuiPaper: {
        defaultProps: { elevation: 0 },
        styleOverrides: {
          // `backgroundImage: none` ist Pflicht: MUI legt im Dunkelmodus einen
          // Aufhell-Verlauf über jedes Paper, und der kämpft mit unserer
          // Flächenleiter.
          root: { backgroundImage: "none" },
          outlined: { borderColor: rand.default },
          elevation1: { boxShadow: hoehe.raised, backgroundColor: flaeche.raised },
          elevation2: { boxShadow: hoehe.overlay, backgroundColor: flaeche.raised },
        },
      },

      MuiCard: {
        defaultProps: { variant: "outlined" },
        styleOverrides: {
          root: {
            borderRadius: radius.lg,
            borderColor: rand.default,
            backgroundColor: flaeche.surface,
            boxShadow: hoehe.raised,
            transition: `border-color ${motion.base}, box-shadow ${motion.base}`,
          },
        },
      },

      MuiCardContent: {
        styleOverrides: {
          root: { padding: 24, "&:last-child": { paddingBottom: 24 } },
        },
      },

      MuiTextField: { defaultProps: { size: "small", fullWidth: true } },

      MuiSelect: {
        styleOverrides: {
          // Platz für den Pfeil. Ohne das schiebt sich der längste Eintrag
          // („Wie mein Gerät") unter das Symbol und wird abgeschnitten —
          // MUI setzt das Padding nur für die eigene Listenvariante, nicht für
          // ein natives `select`.
          select: { paddingRight: "34px !important" },
        },
      },

      MuiOutlinedInput: {
        styleOverrides: {
          root: {
            borderRadius: radius.sm,
            backgroundColor: flaeche.sunken,
            transition: `background-color ${motion.fast}, border-color ${motion.fast}`,
            "& .MuiOutlinedInput-notchedOutline": { borderColor: rand.default },
            "&:hover .MuiOutlinedInput-notchedOutline": { borderColor: rand.strong },
            "&.Mui-focused": { backgroundColor: flaeche.surface },
            "&.Mui-focused .MuiOutlinedInput-notchedOutline": {
              borderWidth: 2,
              borderColor: light ? brand[600] : brand[400],
            },
          },
        },
      },

      MuiFormHelperText: {
        styleOverrides: { root: { marginLeft: 2, lineHeight: 1.5 } },
      },

      MuiInputLabel: { styleOverrides: { root: { fontWeight: 540 } } },

      // Ein Hinweis ohne Rand verschwimmt mit der Seite; gerade Fehler müssen
      // als eigener Block lesbar sein. Die Fläche kommt aus den Bedeutungs-
      // farben, damit ein Fehler im Dunkelmodus nicht blendet.
      MuiAlert: {
        defaultProps: { variant: "outlined" },
        styleOverrides: {
          // Über Selektoren und nicht über `standardSuccess` & Co.: die
          // Override-Schlüssel je Schweregrad heissen von MUI-Version zu
          // MUI-Version anders, die Klassennamen nicht.
          root: {
            borderRadius: radius.md,
            alignItems: "flex-start",
            paddingBlock: 10,
            "&.MuiAlert-standardSuccess, &.MuiAlert-outlinedSuccess": {
              backgroundColor: light ? semantic.granted.surface : semantic.granted.surfaceDark,
              borderColor: light ? semantic.granted.light : semantic.granted.borderDark,
            },
            "&.MuiAlert-standardError, &.MuiAlert-outlinedError": {
              backgroundColor: light ? semantic.revoked.surface : semantic.revoked.surfaceDark,
              borderColor: light ? semantic.revoked.light : semantic.revoked.borderDark,
            },
            "&.MuiAlert-standardWarning, &.MuiAlert-outlinedWarning": {
              backgroundColor: light ? semantic.pending.surface : semantic.pending.surfaceDark,
              borderColor: light ? semantic.pending.light : semantic.pending.borderDark,
            },
            "&.MuiAlert-standardInfo, &.MuiAlert-outlinedInfo": {
              backgroundColor: light ? semantic.info.surface : semantic.info.surfaceDark,
              borderColor: light ? semantic.info.light : semantic.info.borderDark,
            },
          },
        },
      },

      MuiChip: {
        styleOverrides: {
          root: { borderRadius: radius.pill, fontWeight: 560 },
          sizeSmall: { height: 24 },
          outlined: { borderColor: rand.strong },
        },
      },

      MuiTooltip: {
        defaultProps: { arrow: true },
        styleOverrides: {
          tooltip: {
            borderRadius: radius.sm,
            fontSize: "0.8125rem",
            backgroundColor: light ? neutral[900] : neutral[700],
            paddingInline: 10,
            paddingBlock: 6,
          },
          arrow: { color: light ? neutral[900] : neutral[700] },
        },
      },

      MuiLink: {
        defaultProps: { underline: "hover" },
        styleOverrides: { root: { fontWeight: 560 } },
      },

      MuiDialog: {
        styleOverrides: {
          paper: {
            borderRadius: radius.xl,
            boxShadow: hoehe.modal,
            backgroundColor: flaeche.overlay,
            backgroundImage: "none",
          },
        },
      },

      MuiMenu: {
        styleOverrides: {
          paper: {
            borderRadius: radius.md,
            boxShadow: hoehe.overlay,
            backgroundColor: flaeche.overlay,
            border: `1px solid ${rand.default}`,
            backgroundImage: "none",
          },
          list: { paddingBlock: 6 },
        },
      },

      MuiMenuItem: {
        styleOverrides: {
          root: { borderRadius: radius.sm, marginInline: 6, minHeight: 38 },
        },
      },

      MuiDivider: { styleOverrides: { root: { borderColor: rand.subtle } } },

      MuiSkeleton: {
        defaultProps: { animation: "wave" },
        styleOverrides: {
          root: { backgroundColor: rand.subtle, borderRadius: radius.sm },
        },
      },

      MuiPagination: {
        defaultProps: { shape: "rounded", color: "primary" },
      },

      MuiPaginationItem: {
        styleOverrides: {
          root: { borderRadius: radius.sm, fontWeight: 560 },
        },
      },

      MuiAppBar: {
        defaultProps: { elevation: 0, color: "inherit" },
        styleOverrides: {
          root: {
            backgroundColor: flaeche.surface,
            backgroundImage: "none",
            borderBottom: `1px solid ${rand.default}`,
            zIndex: layer.header,
          },
        },
      },

      MuiToolbar: {
        styleOverrides: { root: { minHeight: `${size.headerHeight}px !important` } },
      },

      MuiSwitch: {
        styleOverrides: {
          root: { padding: 8 },
          track: { borderRadius: radius.pill, opacity: 1, backgroundColor: rand.strong },
        },
      },

      MuiTabs: {
        styleOverrides: {
          indicator: { height: 3, borderRadius: `${radius.sm}px ${radius.sm}px 0 0` },
        },
      },

      MuiTab: {
        styleOverrides: { root: { fontWeight: 580, minHeight: 46 } },
      },

      MuiListItemButton: {
        styleOverrides: { root: { borderRadius: radius.sm } },
      },

      MuiAccordion: {
        defaultProps: { disableGutters: true, elevation: 0 },
        styleOverrides: {
          root: {
            backgroundColor: "transparent",
            backgroundImage: "none",
            "&::before": { display: "none" },
          },
        },
      },

      MuiAccordionSummary: {
        styleOverrides: {
          root: { paddingInline: 0, minHeight: 44 },
          content: { marginBlock: 8 },
        },
      },

      MuiAccordionDetails: {
        styleOverrides: { root: { paddingInline: 0, paddingTop: 0 } },
      },
    },
  };
};

/** Fertig gebaut, für die Stellen, die kein `mode` von aussen bekommen. */
export const buildTheme = (mode: PaletteMode) => createTheme(getThemeOptions(mode));
