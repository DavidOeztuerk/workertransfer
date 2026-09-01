/**
 * Die Schriftskala.
 *
 * Inter als variable Schrift, lokal eingebunden (`@fontsource-variable/inter`)
 * und NICHT von einem fremden Server: eine Oberfläche, die über Einwilligungen
 * entscheidet, soll beim Laden nicht die Adresse ihrer Besucher an Dritte
 * geben. Das ist kein Detail, das ist dieselbe Regel wie im Rest des Systems.
 */
export const fontFamily = [
  "'Inter Variable'",
  "Inter",
  "-apple-system",
  "BlinkMacSystemFont",
  "'Segoe UI'",
  "Roboto",
  "sans-serif",
].join(", ");

export const monoFamily = [
  "'JetBrains Mono'",
  "ui-monospace",
  "SFMono-Regular",
  "Menlo",
  "monospace",
].join(", ");

/**
 * Eine kleine Skala mit klaren Sprüngen. Überschriften stehen eng und dunkel,
 * Fliesstext atmet — auf dieser Plattform stehen lange, erklärende Sätze, und
 * die müssen lesbar bleiben.
 */
export const scale = {
  display: { fontSize: "2.5rem", lineHeight: 1.15, fontWeight: 700, letterSpacing: "-0.022em" },
  h1: { fontSize: "1.9rem", lineHeight: 1.2, fontWeight: 680, letterSpacing: "-0.019em" },
  h2: { fontSize: "1.45rem", lineHeight: 1.25, fontWeight: 660, letterSpacing: "-0.015em" },
  h3: { fontSize: "1.15rem", lineHeight: 1.35, fontWeight: 640, letterSpacing: "-0.01em" },
  h4: { fontSize: "1rem", lineHeight: 1.4, fontWeight: 620 },
  body1: { fontSize: "0.975rem", lineHeight: 1.65 },
  body2: { fontSize: "0.875rem", lineHeight: 1.6 },
  caption: { fontSize: "0.8125rem", lineHeight: 1.5 },
  button: { fontSize: "0.9rem", fontWeight: 600, letterSpacing: 0, textTransform: "none" as const },
} as const;
