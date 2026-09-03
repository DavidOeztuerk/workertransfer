/**
 * Die Farbwelt — eine Quelle, aus der Theme und Komponenten lesen.
 *
 * Weg von Grün/Weiß, und nicht aus Geschmack: Grün ist auf einer Plattform,
 * die über Freigaben entscheidet, ein Signal ("erlaubt") und darf deshalb nicht
 * gleichzeitig die Hausfarbe sein. Wer beides mischt, nimmt dem Signal seine
 * Bedeutung.
 *
 * Träger ist ein tiefes Indigo: ruhig, ernst, im Unternehmenskontext lesbar —
 * und weit genug vom generischen SaaS-Blau entfernt, dass die Oberfläche eine
 * eigene Handschrift bekommt. Der Akzent ist Bernstein, warm und sparsam
 * eingesetzt: er gehört der EINEN Handlung, die auf einer Seite zählt.
 *
 * Grün, Rot und Bernstein bleiben dadurch frei für ihre eigentliche Aufgabe:
 * erteilt, zurückgezogen, in Arbeit.
 */

/** Der Träger. 500 ist die Hausfarbe, 600 der Ruhezustand einer Fläche. */
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

/** Der Akzent. Sparsam: er markiert die eine Handlung, die zählt. */
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
 * Die Neutralen — Schiefer mit kühlem Stich, damit sie neben dem Indigo nicht
 * schmutzig wirken. Fast die ganze Oberfläche besteht aus diesen Werten.
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
 * Die Bedeutungsfarben. `granted` und `revoked` sind KEINE Dekoration: sie
 * sagen, ob eine Freigabe gilt, und dürfen deshalb nirgends sonst auftauchen.
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
 * Die Flächen — eine LEITER, kein Paar.
 *
 * <strong>Im Dunkelmodus trägt die Fläche die Tiefe, nicht der Schatten.</strong>
 * Ein Schatten auf dunklem Grund liest sich als Rahmen, nicht als Höhe; das ist
 * der Grund, warum invertierte Themes flach wirken. Stattdessen wird jede
 * Ebene ein paar Prozent heller als ihre Mutter — `canvas` → `surface` →
 * `raised` → `overlay`. Genau so bauen es Linear, Vercel und Radix.
 *
 * <strong>Kein reines Schwarz.</strong> `#000` erzeugt Halation an hellem Text
 * und lässt keine Ebene mehr unter sich zu: der Grund kann nicht dunkler werden
 * als der Grund. Der Boden liegt deshalb bei `#0d0e14`.
 *
 * `sunken` ist die einzige Ebene, die nach unten geht — für Codeblöcke,
 * Eingabefelder im Ruhezustand und alles, was eingelassen wirken soll.
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
 * Die Ränder. Drei Stärken reichen, und mehr wären eine Entscheidung, die
 * niemand konsistent trifft.
 *
 * Im Dunkelmodus tragen sie mehr als im hellen: dort ist der Rand das, was eine
 * Karte von ihrem Grund trennt, weil der Schatten es nicht kann.
 */
export const borders = {
  light: { subtle: "#edeff6", default: neutral[200], strong: neutral[300] },
  dark: { subtle: "#1d2130", default: "#272c3d", strong: "#39405a" },
} as const;

/**
 * Der Fokusring.
 *
 * Sichtbar und in der Hausfarbe, nicht der blasse Vorgabering des Browsers:
 * wer mit der Tastatur navigiert, muss auf einen Blick sehen, wo er steht.
 * Zwei Lagen — ein deckender Kern und ein weicher Hof —, damit er auf hellem
 * wie auf dunklem Grund trägt.
 */
export const focusRing = {
  light: `0 0 0 2px ${neutral[0]}, 0 0 0 4px rgb(107 94 242 / 0.45)`,
  dark: `0 0 0 2px #0d0e14, 0 0 0 4px rgb(164 174 255 / 0.55)`,
} as const;

/**
 * Verläufe — sparsam, und nie hinter Text, den man lesen muss.
 *
 * Genau zwei: einer für die Kopfzone der Marketingseite, einer als dünner
 * Streifen über der Kopfleiste. Ein Verlauf ist Dekoration; drei davon sind
 * eine Behauptung über Wichtigkeit, die niemand eingelöst hat.
 */
export const gradients = {
  brandSoft: `linear-gradient(135deg, ${brand[500]} 0%, ${brand[700]} 55%, ${accent[600]} 140%)`,
  hairline: `linear-gradient(90deg, ${brand[500]} 0%, ${accent[500]} 50%, ${brand[500]} 100%)`,
} as const;
