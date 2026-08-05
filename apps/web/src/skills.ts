// Fähigkeiten als Formulareingabe — für das Profil einer Person und für das,
// was eine Stelle verlangt.
//
// Gemeinsam, weil beide Listen im Browser gegeneinander gehalten werden
// (`jobs/match.ts`). Zwei Zerleger, die auseinanderlaufen, fänden zufällige
// Treffer: schriebe die eine Seite `"Python, Go"` als zwei Einträge und die
// andere als einen, verglichen sich hinterher Sätze mit Wörtern.

/**
 * Kommagetrennt statt Chips: eine Zeile, die man aus dem Lebenslauf einfügen
 * kann. Leeres und Whitespace fällt weg — die Domäne entdoppelt zusätzlich, was
 * hier durchrutscht.
 */
export function parseSkills(raw: string): string[] {
  return raw
    .split(",")
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
}
