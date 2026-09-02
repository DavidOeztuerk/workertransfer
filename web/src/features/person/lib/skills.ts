/**
 * Fähigkeiten als Formulareingabe.
 *
 * Kommagetrennt statt Chips: eine Zeile, die man aus dem Lebenslauf einfügen
 * kann. Leeres und Leerraum fällt weg — die Domäne entdoppelt zusätzlich, was
 * hier durchrutscht.
 *
 * <strong>Sie lehnt nichts ab und erfindet nichts.</strong> Das Vokabular
 * (ADR-0023) benennt um, es schliesst nie: was der Server nicht kennt, bleibt
 * genau so stehen, wie es getippt wurde. Eine Liste erlaubter Fähigkeiten wäre
 * eine Behauptung darüber, welche Arbeit es gibt.
 */
export function parseSkills(raw: string): string[] {
  return raw
    .split(",")
    .map((eintrag) => eintrag.trim())
    .filter((eintrag) => eintrag.length > 0);
}
