/**
 * Berufsfelder und die Belege, die zu ihnen gehören (ADR-0039).
 *
 * <strong>Aus dem Berufsfeld folgt genau eine Sache: was die Oberfläche
 * ANBIETET.</strong> Kein Recht, keine Sichtbarkeit, keine Sortierung, keine
 * Aussage über einen Menschen. Der Server antwortet auf jede Route dieselbe
 * Antwort wie vorher — was hier steht, entscheidet über Menüeinträge und
 * Vorschläge, nie über Zugriff. Verstecken ist keine Zugriffskontrolle.
 *
 * Die Liste ist GESCHLOSSEN, und das ist der Grund für die Liste: aus diesem
 * Feld folgt eine Navigation, und eine Navigation muss für jeden möglichen Wert
 * eine Antwort haben. Sie spiegelt `Berufsfeldwahl` in identity-service — die
 * elf Etiketten sind dort die Wahrheit, hier stehen sie, weil der Browser
 * entscheidet, was er zeigt.
 */

/** Die elf Etiketten, wie sie auf dem Draht und in der Spalte stehen. */
export const BERUFSFELDER = [
  "handwerk",
  "industrie_technik",
  "bau",
  "gesundheit_pflege",
  "logistik_verkehr",
  "gastronomie_hotel",
  "handel_verkauf",
  "buero_verwaltung",
  "it_software",
  "bildung_soziales",
  "sonstiges",
] as const;

export type Berufsfeld = (typeof BERUFSFELDER)[number];

/**
 * `null` heisst „nicht angegeben" und ist der Normalfall.
 *
 * Es ist NICHT dasselbe wie `"sonstiges"`: wer „sonstiges" wählt, hat gewählt.
 * Beide bekommen heute dieselbe neutrale Ansicht — aber weil das für beide die
 * richtige ist, nicht weil sie dasselbe wären.
 */
export type BerufsfeldWahl = Berufsfeld | null;

/** Ist das ein Etikett, das wir kennen? */
export function istBerufsfeld(wert: unknown): wert is Berufsfeld {
  return (
    typeof wert === "string" && (BERUFSFELDER as readonly string[]).includes(wert)
  );
}

/**
 * Was vom Server kommt, als Wahl gelesen.
 *
 * Ein Etikett, das dieser Browser nicht kennt, wird zu `null` und damit zur
 * neutralen Ansicht — nicht zu einem Fehler. Eine ältere Oberfläche gegen einen
 * neueren Server soll weniger anbieten, nicht abstürzen.
 */
export function leseBerufsfeld(wert: unknown): BerufsfeldWahl {
  return istBerufsfeld(wert) ? wert : null;
}

/**
 * Ein Beleg, der einem Feld angeboten wird.
 *
 * `schluessel` ist der Katalogschlüssel für den Namen — hier stehen keine
 * Sätze, weil jeder Text, den ein Mensch liest, aus einem Katalog kommt
 * (ADR-0031).
 */
export interface Belegart {
  schluessel: string;
  /**
   * Darf GENANNT, aber niemals HOCHGELADEN werden.
   *
   * Ein Führungszeugnis ist ein Auszug aus einem Register über Straftaten, ein
   * Gesundheitszeugnis eine ärztliche Feststellung. Beides muss eine Person
   * sagen können („liegt vor"), weil Arbeitgeber danach fragen — und beides
   * gehört auf keinen Server dieser Plattform. Eine Datei, die nie angeboten
   * wird, muss nicht gelöscht werden.
   */
  nurGenannt?: true;
}

/**
 * Die Belege, die JEDES Feld erbt.
 *
 * Die wichtigste Zeile der Tabelle: die feldeigenen Belege kommen dazu, sie
 * ersetzen nichts. Ein Entwickler mit einem Meisterbrief kann ihn hochladen —
 * diese Tabelle entscheidet, was VORGESCHLAGEN wird, nie, was ERLAUBT ist.
 */
const ALLGEMEIN: readonly Belegart[] = [
  { schluessel: "beleg.arbeitszeugnis" },
  { schluessel: "beleg.zertifikat" },
  { schluessel: "beleg.referenz" },
];

const EIGEN: Record<Berufsfeld, readonly Belegart[]> = {
  handwerk: [
    { schluessel: "beleg.gesellenbrief" },
    { schluessel: "beleg.meisterbrief" },
    { schluessel: "beleg.schweisserpass" },
    { schluessel: "beleg.staplerschein" },
    { schluessel: "beleg.arbeitsprobe" },
  ],
  industrie_technik: [
    { schluessel: "beleg.gesellenbrief" },
    { schluessel: "beleg.meisterbrief" },
    { schluessel: "beleg.schweisserpass" },
    { schluessel: "beleg.staplerschein" },
    { schluessel: "beleg.arbeitsprobe" },
  ],
  bau: [
    { schluessel: "beleg.gesellenbrief" },
    { schluessel: "beleg.meisterbrief" },
    { schluessel: "beleg.schweisserpass" },
    { schluessel: "beleg.staplerschein" },
    { schluessel: "beleg.arbeitsprobe" },
  ],
  gesundheit_pflege: [
    { schluessel: "beleg.berufsurkunde" },
    { schluessel: "beleg.fortbildung" },
    { schluessel: "beleg.fuehrungszeugnis", nurGenannt: true },
  ],
  logistik_verkehr: [
    { schluessel: "beleg.fuehrerscheinklasse" },
    { schluessel: "beleg.adrSchein" },
    { schluessel: "beleg.fahrerkarte" },
    { schluessel: "beleg.staplerschein" },
  ],
  gastronomie_hotel: [
    { schluessel: "beleg.gesundheitszeugnis", nurGenannt: true },
    { schluessel: "beleg.ausbildungszeugnis" },
    { schluessel: "beleg.arbeitsprobe" },
  ],
  handel_verkauf: [{ schluessel: "beleg.ausbildungszeugnis" }],
  buero_verwaltung: [{ schluessel: "beleg.ausbildungszeugnis" }],
  it_software: [
    { schluessel: "beleg.github" },
    { schluessel: "beleg.arbeitsprobe" },
  ],
  bildung_soziales: [
    { schluessel: "beleg.ausbildungszeugnis" },
    { schluessel: "beleg.fortbildung" },
    { schluessel: "beleg.fuehrungszeugnis", nurGenannt: true },
  ],
  sonstiges: [],
};

/**
 * Was diesem Feld angeboten wird — das Eigene zuerst, dann das Allgemeine.
 *
 * Ohne Feld: nur das Allgemeine. Wer nichts gewählt hat, sieht die heutige
 * Ansicht, und die kennt kein Berufsfeld.
 */
export function belegartenFuer(feld: BerufsfeldWahl): readonly Belegart[] {
  const eigen = feld === null ? [] : EIGEN[feld];
  const gesehen = new Set(eigen.map((art) => art.schluessel));

  return [...eigen, ...ALLGEMEIN.filter((art) => !gesehen.has(art.schluessel))];
}

/**
 * Die Belege, für die es einen Hochladeknopf gibt.
 *
 * Was `nurGenannt` trägt, fällt hier heraus — und zwar hier und nicht erst in
 * der Ansicht, damit es nicht an einer zweiten Stelle wieder auftauchen kann.
 */
export function hochladbareBelege(feld: BerufsfeldWahl): readonly Belegart[] {
  return belegartenFuer(feld).filter((art) => art.nurGenannt !== true);
}

/**
 * Bietet dieses Feld GitHub an?
 *
 * <strong>Die Route bleibt erreichbar.</strong> Wer `/github` tippt, bekommt
 * die Seite — hier wird nur entschieden, ob sie ANGEBOTEN wird. Ein Konto ohne
 * Berufsfeld bekommt sie weiterhin: es wird niemandem etwas weggenommen, der
 * nicht gewählt hat, und GitHub wird nicht abgewertet (ADR-0039).
 */
export function zeigtGitHub(feld: BerufsfeldWahl): boolean {
  return feld === null || feld === "it_software";
}
