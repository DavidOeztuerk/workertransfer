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
  raised: "0 1px 2px 0 rgb(18 19 28 / 0.04), 0 1px 3px 0 rgb(18 19 28 / 0.06)",
  overlay: "0 4px 12px -2px rgb(18 19 28 / 0.10), 0 2px 6px -2px rgb(18 19 28 / 0.06)",
  modal: "0 18px 44px -12px rgb(18 19 28 / 0.28)",
} as const;
