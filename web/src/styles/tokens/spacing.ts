/**
 * The spacing grid — a 4px base unit, as MUI expects it.
 *
 * Why a scale instead of free values: spacing is what makes a UI calm or
 * restless, and free values become arbitrary by the second screen. Whoever
 * needs a value that is missing adds it HERE.
 */
export const spacingUnit = 4;

/**
 * The side gutter of every page, in spacing units: 16 / 24 / 32 px.
 *
 * ONE value for header, content and footer. Written per component it was
 * `{ xs: 2, sm: 3, md: 4 }` everywhere — meant as 16/24/32 px on an 8px
 * grid, measured as 8/12/16 px on this 4px one: on a phone the cards almost
 * touched the edge while the logo sat further in.
 */
export const gutter = { xs: 4, sm: 6, md: 8 } as const;

/** Radii. Generous but not playful — it remains a working tool. */
export const radius = {
  sm: 6,
  md: 10,
  lg: 14,
  xl: 20,
  pill: 999,
} as const;

/**
 * Shadows, sparing and flat.
 *
 * MUI brings 25 levels, most of them from a time when depth was fashionable.
 * We use three, and the reason is not taste: a shadow should say "this lies
 * above", and when everything has a shadow, it says nothing any more.
 */
export const elevation = {
  light: {
    none: "none",
    raised: "0 1px 2px 0 rgb(18 19 28 / 0.04), 0 1px 3px 0 rgb(18 19 28 / 0.06)",
    hover: "0 2px 4px -1px rgb(18 19 28 / 0.07), 0 6px 16px -6px rgb(18 19 28 / 0.12)",
    overlay: "0 4px 12px -2px rgb(18 19 28 / 0.10), 0 2px 6px -2px rgb(18 19 28 / 0.06)",
    modal: "0 18px 44px -12px rgb(18 19 28 / 0.28)",
  },
  /**
   * In dark mode almost nothing.
   *
   * A shadow on a dark ground is invisible or reads as a frame — there the
   * surface ladder (`surfaces.dark`) carries depth. What remains is a deep drop
   * shadow for things that really lie above everything.
   */
  dark: {
    none: "none",
    raised: "none",
    hover: "none",
    overlay: "0 8px 24px -8px rgb(0 0 0 / 0.60)",
    modal: "0 24px 56px -16px rgb(0 0 0 / 0.75)",
  },
} as const;

/**
 * Transitions. Short, and the same for all.
 *
 * 140 ms is the limit below which a movement reads as "instant" and still does
 * not jerk. Anything longer feels like waiting in a working tool.
 */
export const motion = {
  fast: "120ms cubic-bezier(0.4, 0, 0.2, 1)",
  base: "160ms cubic-bezier(0.4, 0, 0.2, 1)",
  slow: "240ms cubic-bezier(0.4, 0, 0.2, 1)",
} as const;

/**
 * The layers. Written out so that nobody writes `zIndex: 9999` — that number
 * wins exactly until somebody writes `10000`.
 */
export const layer = {
  base: 0,
  sticky: 100,
  header: 1100,
  drawer: 1200,
  dialog: 1300,
  toast: 1400,
} as const;

/** Fixed dimensions needed in several places. */
export const size = {
  headerHeight: 64,
  filterSidebar: 264,
  contentMax: 1240,
  contentNarrow: 760,
} as const;
