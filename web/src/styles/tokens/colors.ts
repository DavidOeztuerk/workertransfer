/**
 * The colour world — one source that the theme and the components read from.
 *
 * Away from green/white, and not out of taste: on a platform that decides
 * about releases, green is a signal ("allowed") and must therefore not also be
 * the house colour. Whoever mixes the two takes the signal's meaning away.
 *
 * The carrier is a deep indigo: calm, serious, readable in a company context —
 * and far enough from generic SaaS blue that the UI gets a hand of its own.
 * The accent is amber, warm and used sparingly: it belongs to the ONE action
 * that counts on a page.
 *
 * Green, red and amber thereby stay free for their actual job: granted,
 * revoked, in progress.
 */

/** The carrier. 500 is the house colour, 600 the resting state of a surface. */
export const brand = {
  50: "#eef1ff",
  100: "#e0e5ff",
  200: "#c7cfff",
  300: "#a4aeff",
  400: "#8184fb",
  500: "#6b5ef2",
  600: "#5a45e6",
  700: "#4c37cb",
  800: "#3f30a4",
  900: "#372e82",
  950: "#221b4c",
} as const;

/** The accent. Sparingly: it marks the one action that counts. */
export const accent = {
  50: "#fff8ea",
  100: "#ffeec5",
  200: "#ffdb8a",
  300: "#ffc24e",
  400: "#ffab24",
  500: "#f9880b",
  600: "#dd6406",
  700: "#b74409",
  800: "#94350e",
  900: "#7a2d0f",
  950: "#461503",
} as const;

/**
 * The neutrals — slate with a cool tint so that they do not look dirty beside
 * the indigo. Almost the whole UI consists of these values.
 */
export const neutral = {
  0: "#ffffff",
  50: "#f8f9fc",
  100: "#f1f2f8",
  200: "#e3e5ef",
  300: "#cbcedd",
  400: "#9ba0b8",
  500: "#6f7594",
  600: "#535873",
  700: "#41455c",
  800: "#2b2e40",
  900: "#1c1e2c",
  950: "#12131c",
} as const;

/**
 * The meaning colours. `granted` and `revoked` are NOT decoration: they say
 * whether a release holds, and must therefore appear nowhere else.
 */
export const semantic = {
  granted: {
    light: "#0f8a4d", main: "#0b7a43", dark: "#08602f",
    surface: "#e6f6ed", surfaceDark: "#0d2a1c", borderDark: "#17553a",
  },
  revoked: {
    light: "#d64545", main: "#c02b2b", dark: "#951f1f",
    surface: "#fdecec", surfaceDark: "#2e1416", borderDark: "#6b2427",
  },
  pending: {
    light: "#c47f0a", main: "#a86a05", dark: "#855104",
    surface: "#fdf4e3", surfaceDark: "#2c2109", borderDark: "#6a4d0d",
  },
  info: {
    light: "#2b7fd6", main: "#1d68b8", dark: "#12518f",
    surface: "#e8f2fd", surfaceDark: "#0e1f33", borderDark: "#1c4370",
  },
} as const;

/**
 * The surfaces — a LADDER, not a pair.
 *
 * <strong>In dark mode the surface carries depth, not the shadow.</strong> A
 * shadow on a dark ground reads as a frame, not as height; that is why
 * inverted themes look flat. Instead every layer becomes a few percent lighter
 * than its parent — `canvas` → `surface` → `raised` → `overlay`. That is
 * exactly how Linear, Vercel and Radix build it.
 *
 * <strong>No pure black.</strong> `#000` produces halation around light text
 * and allows no layer beneath it: the ground cannot become darker than the
 * ground. The floor therefore lies at `#0d0e14`.
 *
 * `sunken` is the only layer that goes down — for code blocks, input fields at
 * rest and everything that should look inset.
 */
export const surfaces = {
  light: {
    canvas: "#f6f7fb",
    surface: neutral[0],
    raised: neutral[0],
    overlay: neutral[0],
    sunken: "#eef0f7",
  },
  dark: {
    canvas: "#0d0e14",
    surface: "#14161f",
    raised: "#1b1e2a",
    overlay: "#232736",
    sunken: "#0a0b11",
  },
} as const;

/**
 * The borders. Three strengths are enough, and more would be a decision
 * nobody makes consistently.
 *
 * In dark mode they carry more than in light: there the border is what
 * separates a card from its ground, because the shadow cannot.
 */
export const borders = {
  light: { subtle: "#edeff6", default: neutral[200], strong: neutral[300] },
  dark: { subtle: "#1d2130", default: "#272c3d", strong: "#39405a" },
} as const;

/**
 * The focus ring.
 *
 * Visible and in the house colour, not the browser's pale default ring:
 * whoever navigates with the keyboard must see at a glance where they are. Two
 * layers — an opaque core and a soft halo — so that it carries on a light as
 * well as on a dark ground.
 */
export const focusRing = {
  light: `0 0 0 2px ${neutral[0]}, 0 0 0 4px rgb(107 94 242 / 0.45)`,
  dark: `0 0 0 2px #0d0e14, 0 0 0 4px rgb(164 174 255 / 0.55)`,
} as const;

/**
 * Gradients — sparingly, and never behind text that has to be read.
 *
 * Exactly two: one for the hero zone of the marketing page, one as a thin
 * stripe above the header bar. A gradient is decoration; three of them are a
 * claim about importance that nobody has redeemed.
 */
export const gradients = {
  brandSoft: `linear-gradient(135deg, ${brand[500]} 0%, ${brand[700]} 55%, ${accent[600]} 140%)`,
  hairline: `linear-gradient(90deg, ${brand[500]} 0%, ${accent[500]} 50%, ${brand[500]} 100%)`,
} as const;
