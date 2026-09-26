import type { Session } from "../../auth/types/session";
import type { PostalAddress } from "../../person/api/civilIdentity";
import type { LetterHead } from "../../../shared/components/LetterSheet";
import type { CompanyProfile } from "../../company/api/companies";
import type { ApplicantContact } from "../api/applications";
import type { Job } from "../api/jobs";

/** The sender of one's own letter: the account's name and one's postal address. */
export function senderFromSession(
  session: Session | null,
  address?: PostalAddress | null,
): LetterHead {
  const name =
    `${session?.givenName ?? ""} ${session?.familyName ?? ""}`.trim() ||
    session?.displayName ||
    "";
  return {
    name,
    lines: address
      ? [
          address.line1,
          address.line2,
          `${address.postalCode} ${address.city}`.trim(),
          address.country,
          address.phone,
        ]
      : [],
  };
}

/** The recipient: the company's address, or the job's place while it has none. */
export function recipientFromCompany(
  profile?: CompanyProfile | null,
  job?: Job | null,
): LetterHead {
  const name = profile?.display_name?.trim() ?? "";
  const street = profile?.line1?.trim() ?? "";
  const postalCodeAndCity = `${profile?.postal_code ?? job?.postal_code ?? ""} ${
    profile?.city?.trim() || job?.location || ""
  }`.trim();
  const country = profile?.country?.trim() ?? "";
  return {
    name,
    lines: [street, postalCodeAndCity, country],
  };
}

/** The sender as the company sees it: the contact sent along with the application. */
export function senderFromContact(contact?: ApplicantContact | null): LetterHead {
  if (!contact) return {};
  return {
    name: contact.full_name,
    lines: [
      contact.line1,
      contact.line2,
      `${contact.postal_code} ${contact.city}`.trim(),
      contact.country,
      contact.phone,
      contact.email,
    ],
  };
}
