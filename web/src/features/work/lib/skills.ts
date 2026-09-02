// Fähigkeiten als Formulareingabe — für das Profil einer Person und für das,
// was eine Stelle verlangt.
//
// Ein Zerleger für beide Seiten, weil beide Listen im Browser gegeneinander
// gehalten werden (`lib/match.ts`). Zwei Zerleger, die auseinanderlaufen,
// fänden zufällige Treffer: schriebe die eine Seite `"Python, Go"` als zwei
// Einträge und die andere als einen, verglichen sich hinterher Sätze mit
// Wörtern.
//
// <strong>Er benennt nicht um und er schließt nichts aus.</strong> Das
// Vereinheitlichen von Schreibweisen („Postgres" = „PostgreSQL") passiert im
// Dienst, in den Fähigkeits-Wertobjekten (ADR-0023) — und dort ist es eine
// Aussage über SPRACHE. Eine Liste erlaubter Fähigkeiten im Browser wäre eine
// Behauptung darüber, welche Arbeit es gibt.

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
