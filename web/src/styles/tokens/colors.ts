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
  granted: { light: "#0f8a4d", main: "#0b7a43", dark: "#08602f", surface: "#e6f6ed" },
  revoked: { light: "#d64545", main: "#c02b2b", dark: "#951f1f", surface: "#fdecec" },
  pending: { light: "#c47f0a", main: "#a86a05", dark: "#855104", surface: "#fdf4e3" },
  info: { light: "#2b7fd6", main: "#1d68b8", dark: "#12518f", surface: "#e8f2fd" },
} as const;
