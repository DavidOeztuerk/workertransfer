/**
 * Occupational fields and their evidence (ADR-0039).
 *
 * From the occupational field follows only what the UI offers — no right, no
 * visibility. The list is closed and mirrors the occupational field choice in
 * identity-service.
 */

/** The eleven labels, as they stand on the wire and in the column. */
export const OCCUPATIONAL_FIELDS = [
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

export type OccupationalField = (typeof OCCUPATIONAL_FIELDS)[number];

/** `null` means "not stated" — not the same as `"sonstiges"`. */
export type OccupationalFieldChoice = OccupationalField | null;

/** Is this a label we know? */
export function isOccupationalField(value: unknown): value is OccupationalField {
  return (
    typeof value === "string" && (OCCUPATIONAL_FIELDS as readonly string[]).includes(value)
  );
}

/**
 * What comes from the server, read as a choice. The unknown becomes `null`: an
 * older UI should offer less, not crash.
 */
export function readOccupationalField(value: unknown): OccupationalFieldChoice {
  return isOccupationalField(value) ? value : null;
}

/** A kind of evidence offered to a field. `labelKey` is the catalogue key. */
export interface EvidenceKind {
  labelKey: string;
  /**
   * May be named but never uploaded — police clearance, health certificate. A
   * file that is never offered does not have to be deleted.
   */
  namedOnly?: true;
}

/** The evidence every field inherits. The field's own comes on top. */
const COMMON: readonly EvidenceKind[] = [
  { labelKey: "credentials.employmentReference" },
  { labelKey: "credentials.certificate" },
  { labelKey: "credentials.reference" },
];

const FIELD_SPECIFIC: Record<OccupationalField, readonly EvidenceKind[]> = {
  handwerk: [
    { labelKey: "credentials.journeymanCertificate" },
    { labelKey: "credentials.masterCraftsmanCertificate" },
    { labelKey: "credentials.welderPass" },
    { labelKey: "credentials.forkliftLicence" },
    { labelKey: "credentials.workSamplePhotos" },
  ],
  industrie_technik: [
    { labelKey: "credentials.journeymanCertificate" },
    { labelKey: "credentials.masterCraftsmanCertificate" },
    { labelKey: "credentials.welderPass" },
    { labelKey: "credentials.forkliftLicence" },
    { labelKey: "credentials.workSamplePhotos" },
  ],
  bau: [
    { labelKey: "credentials.journeymanCertificate" },
    { labelKey: "credentials.masterCraftsmanCertificate" },
    { labelKey: "credentials.welderPass" },
    { labelKey: "credentials.forkliftLicence" },
    { labelKey: "credentials.workSamplePhotos" },
  ],
  gesundheit_pflege: [
    { labelKey: "credentials.professionalLicence" },
    { labelKey: "credentials.trainingCertificate" },
    { labelKey: "credentials.policeClearance", namedOnly: true },
  ],
  logistik_verkehr: [
    { labelKey: "credentials.drivingLicenceClasses" },
    { labelKey: "credentials.adrCertificate" },
    { labelKey: "credentials.driverCard" },
    { labelKey: "credentials.forkliftLicence" },
  ],
  gastronomie_hotel: [
    { labelKey: "credentials.healthCertificate", namedOnly: true },
    { labelKey: "credentials.apprenticeshipCertificate" },
    { labelKey: "credentials.workSamplePhotos" },
  ],
  handel_verkauf: [{ labelKey: "credentials.apprenticeshipCertificate" }],
  buero_verwaltung: [{ labelKey: "credentials.apprenticeshipCertificate" }],
  it_software: [
    { labelKey: "credentials.github" },
    { labelKey: "credentials.workSamplePhotos" },
  ],
  bildung_soziales: [
    { labelKey: "credentials.apprenticeshipCertificate" },
    { labelKey: "credentials.trainingCertificate" },
    { labelKey: "credentials.policeClearance", namedOnly: true },
  ],
  sonstiges: [],
};

/** What this field is offered — its own first, then the common ones. */
export function evidenceKindsFor(field: OccupationalFieldChoice): readonly EvidenceKind[] {
  const own = field === null ? [] : FIELD_SPECIFIC[field];
  const seen = new Set(own.map((kind) => kind.labelKey));

  return [...own, ...COMMON.filter((kind) => !seen.has(kind.labelKey))];
}

/** The evidence with an upload button. `namedOnly` drops out here, not only in the view. */
export function uploadableEvidenceKinds(field: OccupationalFieldChoice): readonly EvidenceKind[] {
  return evidenceKindsFor(field).filter((kind) => kind.namedOnly !== true);
}

/**
 * Does this field offer GitHub? The route stays reachable — this only decides
 * whether it is offered. Without an occupational field: yes.
 */
export function offersGitHub(field: OccupationalFieldChoice): boolean {
  return field === null || field === "it_software";
}
