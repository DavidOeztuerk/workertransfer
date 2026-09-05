import type { Repository } from "../api/github";

/**
 * Wie viele Vorschläge höchstens angeboten werden.
 *
 * Wer hundert Repositories hat, hat schnell dreihundert verschiedene Topics.
 * Eine Wand aus Wörtern liest niemand, und wer sie trotzdem alle übernimmt,
 * behauptet über sich etwas, das er nicht gemeint hat.
 */
const HOECHSTENS = 24;

/** Eine Quelle: die Wörter eines Repositories, einer Station, einer Arbeit. */
export interface Quelle {
  woerter: readonly string[];
  /**
   * Hat die Person diese Wörter SELBST hingeschrieben?
   *
   * Die Technologien einer Lebenslauf-Station ja; die Topics eines Repositories
   * auch, aber an einem Artefakt statt über sich. Der Unterschied entscheidet
   * die Reihenfolge — siehe <c>vorschlaegeAus</c>.
   */
  selbstGetippt: boolean;
}

/**
 * Aus Belegen werden VORSCHLÄGE — nie Nennungen.
 *
 * <strong>Das ist die Naht, an der ADR-0022 hängt.</strong> Was in einem
 * Repository vorkommt, ist eine Tatsache über ein Repository. Dass eine Person
 * etwas kann, ist eine Aussage über einen Menschen, und die darf nur sie selbst
 * treffen. Diese Funktion überbrückt die beiden nicht — sie legt nur Wörter
 * bereit, die ein Mensch mit einem Klick übernimmt oder stehen lässt.
 *
 * Deshalb schreibt sie auch nichts: sie füllt das Formularfeld, und gültig wird
 * es erst mit „Speichern". Zwei Handlungen, damit keine davon aus Versehen
 * passiert.
 *
 * <strong>Selbst Getipptes steht vorn, und das ist gemessen.</strong> Ohne diese
 * Stufe ordnete allein die Häufigkeit — und ein Konto mit hundert Repositories
 * drückte die drei Technologien aus dem eigenen Lebenslauf aus den obersten
 * vierundzwanzig heraus. Gemessen an einem echten Konto: „Kafka" stand in zwei
 * Stationen, „nodejs" in Dutzenden Repositories, und die eigene Angabe war
 * nicht mehr zu sehen. Ein Wort, das jemand über SEINE ARBEIT geschrieben hat,
 * wiegt schwerer als eins, das an einem Artefakt gefunden wurde — dieselbe
 * Rangfolge wie „Genannt vor Belegt".
 *
 * <strong>Die Reihenfolge ordnet WÖRTER, nie Menschen.</strong> Eine Zahl daraus
 * wird nirgends gezeigt, und aus der Reihenfolge folgt keine Rangfolge über
 * irgendjemanden.
 *
 * @param quellen Je Repository, Station oder Arbeit eine Quelle.
 * @param schonGenannt Was die Person bereits selbst ins Profil getippt hat.
 * @returns Wörter, die sie noch nicht genannt hat — die nächstliegenden zuerst.
 */
export function vorschlaegeAus(
  quellen: readonly Quelle[],
  schonGenannt: readonly string[]
): string[] {
  // Der Vergleich ist unempfindlich gegen Gross- und Kleinschreibung: wer
  // „TypeScript" im Profil hat, soll nicht „typescript" vorgeschlagen bekommen.
  // Das Vokabular (ADR-0023) vereinheitlicht erst beim Speichern im Dienst.
  const bekannt = new Set(schonGenannt.map((wort) => wort.trim().toLowerCase()));

  const gefunden = new Map<
    string,
    { wort: string; quellen: number; selbstGetippt: boolean }
  >();

  for (const quelle of quellen) {
    // Je QUELLE einmal zählen, auch wenn ein Wort dort mehrfach steht — etwa
    // als Topic und als Sprache desselben Repositories. Sonst zählte die
    // Schreibweise mit, nicht das Projekt.
    const woerter = new Set(
      quelle.woerter.map((wort) => wort.trim()).filter((wort) => wort.length > 0)
    );

    for (const wort of woerter) {
      const schluessel = wort.toLowerCase();
      if (bekannt.has(schluessel)) continue;

      const eintrag = gefunden.get(schluessel);

      if (eintrag === undefined) {
        gefunden.set(schluessel, {
          wort,
          quellen: 1,
          selbstGetippt: quelle.selbstGetippt,
        });
      } else {
        eintrag.quellen += 1;
        // Einmal selbst getippt bleibt selbst getippt: dass dasselbe Wort auch
        // an einem Repository klebt, macht die eigene Angabe nicht schwächer.
        eintrag.selbstGetippt ||= quelle.selbstGetippt;
      }
    }
  }

  return [...gefunden.values()]
    .sort(
      (a, b) =>
        Number(b.selbstGetippt) - Number(a.selbstGetippt) ||
        b.quellen - a.quellen ||
        a.wort.localeCompare(b.wort)
    )
    .slice(0, HOECHSTENS)
    .map((eintrag) => eintrag.wort);
}

/**
 * Repositories als Quellen.
 *
 * <c>selbstGetippt: false</c> — Topics und Sprachen kleben an einem Artefakt.
 * Der Besitzer hat sie hingeschrieben, aber über ein Projekt, nicht über sich.
 */
export function ausRepositories(
  repositories: readonly Repository[]
): Quelle[] {
  return repositories.map((repository) => ({
    woerter: [...repository.topics, ...repository.languages],
    selbstGetippt: false,
  }));
}

/**
 * Arbeiten als Quellen.
 *
 * <c>selbstGetippt: true</c> — „damit habe ich das gebaut" ist ein Satz über
 * die eigene Arbeit, genau wie an einer Station.
 */
export function ausArbeiten(
  arbeiten: readonly { technologies: string[] }[]
): Quelle[] {
  return arbeiten.map((arbeit) => ({
    woerter: arbeit.technologies,
    selbstGetippt: true,
  }));
}

/**
 * Lebenslauf-Stationen als Quellen.
 *
 * <c>selbstGetippt: true</c> — „damit habe ich dort gearbeitet" ist ein Satz
 * über die eigene Arbeit, nicht über ein Artefakt.
 */
export function ausStationen(
  stationen: readonly { technologies: string[] }[]
): Quelle[] {
  return stationen.map((station) => ({
    woerter: station.technologies,
    selbstGetippt: true,
  }));
}
