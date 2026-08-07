// Der Abgleich zwischen dem eigenen Profil und dem, was eine Stelle verlangt.
//
// Er steht hier und nicht auf dem Server, und das ist die tragende
// Entscheidung dieses Schnitts: **die Passung existiert nirgends als
// Datensatz.** Kein Feld, keine Tabelle, keine Kennzahl, die später jemand
// auswertet oder einem Unternehmen zeigt. Sie lebt, solange die Seite offen
// ist, und verschwindet mit ihr — was es nicht gibt, kann auch nicht
// ausgewertet werden.
//
// Und sie geht in die andere Richtung als das übliche „Matching": sie ordnet
// Stellen für eine Person, nicht Menschen für ein Unternehmen. Eine Rangfolge
// von Menschen bliebe eine Rangfolge von Menschen, gleich woraus sie gerechnet
// wurde (ADR-0022).

export interface SkillMatch {
  /** Aus der Liste der Stelle: was die Person laut Profil kann. */
  have: string[];
  /** Aus derselben Liste: was fehlt. Das ist der Teil, der etwas nützt. */
  missing: string[];
}

// `toLowerCase` statt `toLocaleLowerCase`: letzteres macht aus „I" im
// Türkischen ein „ı", und die Sprache des Browsers hätte dann Einfluss darauf,
// ob jemand als passend gilt.
const key = (skill: string): string => skill.trim().toLowerCase();

/**
 * Teilt die Liste der Stelle in Haken und Lücken.
 *
 * Bewusst **keine** Prozentzahl: eine sieht aus wie eine Messung und ist eine
 * Division — und sie verschweigt genau das, was zählt, nämlich WELCHE
 * Fähigkeit fehlt. Die Liste sagt es, und damit weiß die Person, was sie tun
 * könnte.
 *
 * Verglichen wird auf Gleichheit, nicht auf Enthaltensein: „Java" ist nicht
 * „JavaScript", und ein Treffer, den niemand behauptet hat, schickt jemanden
 * mit einer falschen Auskunft in ein Gespräch.
 */
export function matchSkills(jobSkills: string[], profileSkills: string[]): SkillMatch {
  const owned = new Set(profileSkills.map(key).filter((skill) => skill !== ""));
  const have: string[] = [];
  const missing: string[] = [];

  // Die Reihenfolge und die Schreibweise sind die der Ausschreibung. Sie mit
  // den Wörtern der Person zu überschreiben wäre eine stille Umdeutung dessen,
  // was das Unternehmen geschrieben hat.
  for (const skill of jobSkills) {
    const cleaned = skill.trim();
    if (cleaned === "") continue;
    (owned.has(key(cleaned)) ? have : missing).push(cleaned);
  }

  return { have, missing };
}
