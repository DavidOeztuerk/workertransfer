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
 * The theme — built from the tokens, not beside them.
 *
 * Every colour value comes from `tokens/colors`. A literal in a component is
 * therefore always a defect and never an exception: the second source is
 * exactly what drifts apart at the next dark mode.
 */
export const getThemeOptions = (mode: PaletteMode): ThemeOptions => {
  const light = mode === "light";

  // The three ladders almost every rule below reads from. They stand here once
  // so that no component invents its own layer.
  const surface = light ? surfaces.light : surfaces.dark;
  const border = light ? borders.light : borders.dark;
  const shadow = light ? elevation.light : elevation.dark;

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
        default: surface.canvas,
        paper: surface.surface,
      },
      text: {
        primary: light ? neutral[900] : neutral[100],
        secondary: light ? neutral[600] : neutral[400],
        disabled: light ? neutral[400] : neutral[600],
      },
      divider: border.default,
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

          // The focus ring is operability, not ornament: whoever navigates with
          // the keyboard must see at all times where they are. MUI's default is
          // invisible on a coloured ground in several places. Two layers, so
          // that it carries on a light as well as on a dark ground.
          //
          // NOT on form fields. An `input`, `select` or `textarea` sits inside
          // an `OutlinedInput`, and that already draws a 2px frame around
          // itself on focus. Both together gave two frames one inside the other
          // — one at the field, one at the box around it — and because the
          // outer one is round and the inner one square, it looked crooked.
          // Measured at the language choice and the filters.
          "*:focus-visible:not(input):not(select):not(textarea)": {
            outline: "none",
            boxShadow: light ? focusRing.light : focusRing.dark,
            borderRadius: radius.sm,
          },

          // The field itself gets no ring of its own: the box around it is the
          // focus, and it is big enough to be seen.
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

          // A narrow scrollbar in the UI's colours. The browser's default is
          // light in dark mode and cuts a white stripe into the page.
          "*::-webkit-scrollbar": { width: 10, height: 10 },
          "*::-webkit-scrollbar-thumb": {
            backgroundColor: border.strong,
            borderRadius: radius.pill,
            border: `2px solid ${surface.canvas}`,
          },
          "*::-webkit-scrollbar-track": { background: "transparent" },

          // Whoever set "reduce motion" means it.
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
            // A press point. Tiny, but it is the difference between "clicked"
            // and "maybe clicked".
            "&:active": { transform: "translateY(0.5px)" },
          },
          sizeSmall: { minHeight: 34, paddingInline: 14 },
          sizeLarge: { minHeight: 48, paddingInline: 26, fontSize: "1rem" },
          contained: {
            boxShadow: "none",
            "&:hover": { boxShadow: shadow.hover },
          },
          outlined: {
            borderColor: border.strong,
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
          // `backgroundImage: none` is mandatory: in dark mode MUI lays a
          // lightening gradient over every Paper, and that fights our surface
          // ladder.
          root: { backgroundImage: "none" },
          outlined: { borderColor: border.default },
          elevation1: { boxShadow: shadow.raised, backgroundColor: surface.raised },
          elevation2: { boxShadow: shadow.overlay, backgroundColor: surface.raised },
        },
      },

      MuiCard: {
        defaultProps: { variant: "outlined" },
        styleOverrides: {
          root: {
            borderRadius: radius.lg,
            borderColor: border.default,
            backgroundColor: surface.surface,
            boxShadow: shadow.raised,
            transition: `border-color ${motion.base}, box-shadow ${motion.base}`,
          },
        },
      },

      MuiCardContent: {
        styleOverrides: {
          root: { padding: 24, "&:last-child": { paddingBottom: 24 } },
        },
      },

      // THE LABEL STANDS ABOVE THE FIELD, ALWAYS.
      //
      // MUI's default is a floating label: it sits IN the empty field and moves
      // onto the outline while typing. That holds as long as all fields are
      // alike — and select fields never are: a `select` always shows a value,
      // so its label stands on top from the start.
      //
      // Measured at the filter bar: three text fields with the label IN the
      // field beside two select fields with the label on the outline, and
      // because the field has a filled ground, the text there lay not in a gap
      // but on the line. Two patterns in one column, and neither looked
      // intended.
      MuiTextField: { defaultProps: { size: "small", fullWidth: true } },

      MuiInputLabel: {
        // `shrink` as the DEFAULT. Without it the position depends on whether a
        // field happens to be filled, and that is exactly the difference that
        // produced the two patterns.
        defaultProps: { shrink: true },
        styleOverrides: {
          root: {
            maxWidth: "100%",
            fontSize: "0.8125rem",
            fontWeight: 580,
            lineHeight: 1.4,
            color: light ? neutral[700] : neutral[300],
            // Focus colours the FRAME, not the label. A label that changes
            // colour pulls the eye away from the field being written in.
            "&.Mui-focused": { color: light ? neutral[700] : neutral[300] },
          },
          // THIS rule is the effective one. `root` alone is not enough: MUI's
          // `outlined` variant sets `position: absolute` and a `transform` with
          // higher specificity, and an override at `root` only looks right in
          // the source and has no effect — measured at exactly this bar.
          outlined: {
            position: "static",
            transform: "none",
            marginBottom: 6,
            "&.MuiInputLabel-shrink": { position: "static", transform: "none" },
          },
        },
      },

      // NO NATIVE SELECT ANY MORE, and the reason is measured.
      //
      // The old comment here claimed MUI's own select would be "operated as a
      // select neither by `selectOption` nor by a screen reader". The second
      // half is false: MUI draws `role="combobox"` with `aria-expanded` and a
      // `role="listbox"`, and screen readers read exactly that as a select.
      // Only Playwright's `selectOption` does not apply — `e2e/stack.ts` holds
      // a helper for that.
      //
      // What a native field costs shows in a screenshot: macOS lays its own
      // window over the field, shifts it to the left so that the CHOSEN entry
      // comes to lie over the field, and draws it in system colours. Different
      // on every operating system, and nowhere like the rest of this UI.
      MuiSelect: {
        defaultProps: {
          // No darkening backdrop: a list with four entries is not a dialog.
          MenuProps: { slotProps: { backdrop: { invisible: true } } },

          // "Any" is a choice and has to show.
          //
          // For an empty value MUI draws NOTHING into the closed field — the
          // entry `<MenuItem value="">…</MenuItem>` stays invisible, and the
          // field looks as if nothing were set. A native `select` showed it;
          // in the switch that would have been lost silently.
          displayEmpty: true,
        },
      },

      MuiOutlinedInput: {
        styleOverrides: {
          // No notch in the outline: the label stands above it, so there is
          // nothing to leave free. `top: 0` is mandatory — the outline is built
          // to carry a label inside it and would otherwise sit half a line
          // height too low.
          notchedOutline: { top: 0, "& legend": { display: "none" } },
          root: {
            borderRadius: radius.sm,
            backgroundColor: surface.sunken,
            transition: `background-color ${motion.fast}, border-color ${motion.fast}`,
            "& .MuiOutlinedInput-notchedOutline": { borderColor: border.default },
            "&:hover .MuiOutlinedInput-notchedOutline": { borderColor: border.strong },
            "&.Mui-focused": { backgroundColor: surface.surface },
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


      // A notice without a border blurs into the page; errors in particular
      // must read as a block of their own. The surface comes from the meaning
      // colours so that an error does not dazzle in dark mode.
      MuiAlert: {
        defaultProps: { variant: "outlined" },
        styleOverrides: {
          // Via selectors and not via `standardSuccess` & co.: the override
          // keys per severity change names from MUI version to MUI version,
          // the class names do not.
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
          outlined: { borderColor: border.strong },
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
            boxShadow: shadow.modal,
            backgroundColor: surface.overlay,
            backgroundImage: "none",
          },
        },
      },

      MuiMenu: {
        styleOverrides: {
          paper: {
            borderRadius: radius.md,
            boxShadow: shadow.overlay,
            backgroundColor: surface.overlay,
            border: `1px solid ${border.default}`,
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

      MuiDivider: { styleOverrides: { root: { borderColor: border.subtle } } },

      MuiSkeleton: {
        defaultProps: { animation: "wave" },
        styleOverrides: {
          root: { backgroundColor: border.subtle, borderRadius: radius.sm },
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
            backgroundColor: surface.surface,
            backgroundImage: "none",
            borderBottom: `1px solid ${border.default}`,
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
          track: { borderRadius: radius.pill, opacity: 1, backgroundColor: border.strong },
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

/** Ready built, for the places that get no `mode` from outside. */
export const buildTheme = (mode: PaletteMode) => createTheme(getThemeOptions(mode));
