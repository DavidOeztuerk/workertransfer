import type { Session } from "../../auth/types/session";
import type { Anschrift } from "../../person/api/zivilidentitaet";
import type { Briefkopf } from "../../../shared/components/Briefbogen";
import type { CompanyProfile } from "../../company/api/companies";
import type { ApplicantContact } from "../api/applications";
import type { Job } from "../api/jobs";

export function kopfAusSitzung(session: Session | null, anschrift?: Anschrift | null): Briefkopf {
  const name =
    `${session?.givenName ?? ""} ${session?.familyName ?? ""}`.trim() ||
    session?.displayName ||
    "";
  return {
    name,
    zeilen: anschrift
      ? [
          anschrift.line1,
          anschrift.line2,
          `${anschrift.postalCode} ${anschrift.city}`.trim(),
          anschrift.country,
          anschrift.phone,
        ]
      : [],
  };
}

export function empfaengerAusFirma(
  profil?: CompanyProfile | null,
  stelle?: Job | null,
): Briefkopf {
  const name = profil?.display_name?.trim() ?? "";
  const strasse = profil?.line1?.trim() ?? "";
  const plzOrt = `${profil?.postal_code ?? stelle?.postal_code ?? ""} ${
    profil?.city?.trim() || stelle?.location || ""
  }`.trim();
  const land = profil?.country?.trim() ?? "";
  return {
    name,
    zeilen: [strasse, plzOrt, land],
  };
}

export function kopfAusKontakt(kontakt?: ApplicantContact | null): Briefkopf {
  if (!kontakt) return {};
  return {
    name: kontakt.full_name,
    zeilen: [
      kontakt.line1,
      kontakt.line2,
      `${kontakt.postal_code} ${kontakt.city}`.trim(),
      kontakt.country,
      kontakt.phone,
      kontakt.email,
    ],
  };
}
