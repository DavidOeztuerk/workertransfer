/**
 * Das Abstandsraster — eine 4px-Grundeinheit, wie MUI sie erwartet.
 *
 * Warum eine Skala statt freier Werte: Abstände sind das, was eine Oberfläche
 * ruhig oder unruhig macht, und freie Werte werden beim zweiten Bildschirm
 * beliebig. Wer hier einen Wert braucht, der fehlt, ergänzt ihn HIER.
 */
export const spacingUnit = 4;

/** Radien. Grosszügig, aber nicht verspielt — es bleibt ein Arbeitswerkzeug. */
export const radius = {
  sm: 6,
  md: 10,
  lg: 14,
  xl: 20,
  pill: 999,
} as const;

/**
 * Schatten, sparsam und flach.
 *
 * MUI bringt 25 Stufen mit, von denen die meisten aus einer Zeit stammen, als
 * Tiefe modisch war. Wir benutzen drei, und der Grund ist nicht Geschmack: ein
 * Schatten soll sagen "das hier liegt darüber", und wenn alles Schatten hat,
 * sagt er nichts mehr.
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
   * Im Dunkelmodus fast nichts.
   *
   * Ein Schatten auf dunklem Grund ist unsichtbar oder liest sich als Rahmen —
   * die Tiefe trägt dort die Flächenleiter (`surfaces.dark`). Was bleibt, ist
   * ein tiefer Schlagschatten für Dinge, die wirklich über allem liegen.
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
 * Übergänge. Kurz, und für alle dasselbe.
 *
 * 140 ms ist die Grenze, unterhalb derer eine Bewegung als „sofort" gelesen
 * wird und trotzdem nicht ruckt. Alles Längere fühlt sich in einem
 * Arbeitswerkzeug nach Warten an.
 */
export const motion = {
  fast: "120ms cubic-bezier(0.4, 0, 0.2, 1)",
  base: "160ms cubic-bezier(0.4, 0, 0.2, 1)",
  slow: "240ms cubic-bezier(0.4, 0, 0.2, 1)",
} as const;

/**
 * Die Ebenen. Ausgeschrieben, damit niemand `zIndex: 9999` schreibt — die Zahl
 * gewinnt nämlich genau so lange, bis jemand `10000` schreibt.
 */
export const layer = {
  base: 0,
  sticky: 100,
  header: 1100,
  drawer: 1200,
  dialog: 1300,
  toast: 1400,
} as const;

/** Feste Masse, die an mehreren Stellen gebraucht werden. */
export const size = {
  headerHeight: 64,
  filterSidebar: 264,
  contentMax: 1240,
  contentNarrow: 760,
} as const;
