// Asking for the CV.
//
// The request names only the subject id — which company asks stands in the
// token, and which permission follows from it the server decides. The UI never
// builds a capability string.
//
// <strong>The request is not the permission.</strong> "Granted" means "was
// granted once", not "holds now" — that is why a request has neither an active
// flag nor a revocation time. After a revocation it stays granted and the read
// still comes back empty. That is the design, not a defect.

import { request } from "../../../core/api/client";
import { RESUME_BASE_URL } from "../../../env";
import { type Failure, interpretError } from "../../../shared/api/errors";

export interface ResumeRequest {
  id: string;
  subject_id: string;
  tenant_id: string;
  status: string;
  created_at: string;
  answered_at: string | null;
}

export type ResumeRequestError =
  | "already-asked"
  | "no-company"
  | "not-available"
  | "unavailable"
  | "offline";

export type ResumeRequestResult = { ok: true; request: ResumeRequest } | Failure<ResumeRequestError>;

export async function requestResume(subjectId: string): Promise<ResumeRequestResult> {
  const answer = await request<ResumeRequest>(
    RESUME_BASE_URL,
    `/resumes/${subjectId}/requests`,
    { method: "POST" },
    "errors.requestNotSent"
  );
  if (answer.ok) return { ok: true, request: answer.value };
  return interpretError<ResumeRequestError>(
    answer.error,
    {
      0: { reason: "offline", titleKey: "errors.noConnection" },
      409: { reason: "already-asked", titleKey: "errors.alreadyAsked" },
      403: {
        reason: "no-company",
        titleKey: "errors.onlyCompaniesRequestResume",
        detailKey: "errors.chooseCompany",
      },
      // Asking requires the PROFILE release, never the existence of a CV: "has
      // already written one" is a fact about the person nobody should be able
      // to probe for.
      404: { reason: "not-available", titleKey: "errors.personNotRequestable" },
      503: {
        reason: "unavailable",
        titleKey: "errors.ledgerSilent",
        detailKey: "errors.tryLater",
      },
    },
    "offline"
  );
}
