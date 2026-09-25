/**
 * Berufsfelder und ihre Belege (ADR-0039).
 *
 * Aus dem Berufsfeld folgt nur, was die Oberfläche anbietet — kein Recht, keine
 * Sichtbarkeit. Die Liste ist geschlossen und spiegelt `Berufsfeldwahl` in
 * identity-service.
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

/** `null` heisst „nicht angegeben" — nicht dasselbe wie `"sonstiges"`. */
export type BerufsfeldWahl = Berufsfeld | null;

/** Ist das ein Etikett, das wir kennen? */
export function istBerufsfeld(wert: unknown): wert is Berufsfeld {
  return (
    typeof wert === "string" && (BERUFSFELDER as readonly string[]).includes(wert)
  );
}

/**
 * Was vom Server kommt, als Wahl gelesen. Unbekanntes wird `null`: eine ältere
 * Oberfläche soll weniger anbieten, nicht abstürzen.
 */
export function leseBerufsfeld(wert: unknown): BerufsfeldWahl {
  return istBerufsfeld(wert) ? wert : null;
}

/** Ein Beleg, der einem Feld angeboten wird. `schluessel` ist der Katalogschlüssel. */
export interface Belegart {
  schluessel: string;
  /**
   * Darf genannt, aber nie hochgeladen werden — Führungszeugnis,
   * Gesundheitszeugnis. Eine Datei, die nie angeboten wird, muss nicht
   * gelöscht werden.
   */
  nurGenannt?: true;
}

/** Die Belege, die jedes Feld erbt. Die feldeigenen kommen dazu. */
const ALLGEMEIN: readonly Belegart[] = [
  { schluessel: "credentials.employmentReference" },
  { schluessel: "credentials.certificate" },
  { schluessel: "credentials.reference" },
];

const EIGEN: Record<Berufsfeld, readonly Belegart[]> = {
  handwerk: [
    { schluessel: "credentials.journeymanCertificate" },
    { schluessel: "credentials.masterCraftsmanCertificate" },
    { schluessel: "credentials.welderPass" },
    { schluessel: "credentials.forkliftLicence" },
    { schluessel: "credentials.workSamplePhotos" },
  ],
  industrie_technik: [
    { schluessel: "credentials.journeymanCertificate" },
    { schluessel: "credentials.masterCraftsmanCertificate" },
    { schluessel: "credentials.welderPass" },
    { schluessel: "credentials.forkliftLicence" },
    { schluessel: "credentials.workSamplePhotos" },
  ],
  bau: [
    { schluessel: "credentials.journeymanCertificate" },
    { schluessel: "credentials.masterCraftsmanCertificate" },
    { schluessel: "credentials.welderPass" },
    { schluessel: "credentials.forkliftLicence" },
    { schluessel: "credentials.workSamplePhotos" },
  ],
  gesundheit_pflege: [
    { schluessel: "credentials.professionalLicence" },
    { schluessel: "credentials.trainingCertificate" },
    { schluessel: "credentials.policeClearance", nurGenannt: true },
  ],
  logistik_verkehr: [
    { schluessel: "credentials.drivingLicenceClasses" },
    { schluessel: "credentials.adrCertificate" },
    { schluessel: "credentials.driverCard" },
    { schluessel: "credentials.forkliftLicence" },
  ],
  gastronomie_hotel: [
    { schluessel: "credentials.healthCertificate", nurGenannt: true },
    { schluessel: "credentials.apprenticeshipCertificate" },
    { schluessel: "credentials.workSamplePhotos" },
  ],
  handel_verkauf: [{ schluessel: "credentials.apprenticeshipCertificate" }],
  buero_verwaltung: [{ schluessel: "credentials.apprenticeshipCertificate" }],
  it_software: [
    { schluessel: "credentials.github" },
    { schluessel: "credentials.workSamplePhotos" },
  ],
  bildung_soziales: [
    { schluessel: "credentials.apprenticeshipCertificate" },
    { schluessel: "credentials.trainingCertificate" },
    { schluessel: "credentials.policeClearance", nurGenannt: true },
  ],
  sonstiges: [],
};

/** Was diesem Feld angeboten wird — das Eigene zuerst, dann das Allgemeine. */
export function belegartenFuer(feld: BerufsfeldWahl): readonly Belegart[] {
  const eigen = feld === null ? [] : EIGEN[feld];
  const gesehen = new Set(eigen.map((art) => art.schluessel));

  return [...eigen, ...ALLGEMEIN.filter((art) => !gesehen.has(art.schluessel))];
}

/** Die Belege mit Hochladeknopf. `nurGenannt` fällt hier heraus, nicht erst in der Ansicht. */
export function hochladbareBelege(feld: BerufsfeldWahl): readonly Belegart[] {
  return belegartenFuer(feld).filter((art) => art.nurGenannt !== true);
}

/**
 * Bietet dieses Feld GitHub an? Die Route bleibt erreichbar — hier wird nur
 * entschieden, ob sie angeboten wird. Ohne Berufsfeld: ja.
 */
export function zeigtGitHub(feld: BerufsfeldWahl): boolean {
  return feld === null || feld === "it_software";
}
