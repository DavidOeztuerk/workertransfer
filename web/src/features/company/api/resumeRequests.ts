// Nach dem Lebenslauf fragen.
//
// Die Anfrage nennt nur die Subject-ID — welches Unternehmen fragt, steht im
// Token, und welche Berechtigung daraus folgt, entscheidet der Server. Die
// Oberfläche baut nie einen Capability-String.
//
// <strong>Die Anfrage ist nicht die Erlaubnis.</strong> „Gewährt" heißt „wurde
// einmal gewährt", nicht „gilt jetzt" — deshalb hat ein Vorgang weder ein
// Aktiv-Kennzeichen noch einen Widerrufszeitpunkt. Nach einem Widerruf bleibt
// er gewährt und der Abruf kommt trotzdem leer zurück. Das ist der Entwurf,
// kein Fehler.

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

export type AnfrageFehler =
  | "already-asked"
  | "no-company"
  | "not-available"
  | "unavailable"
  | "offline";

export type AnfrageErgebnis = { ok: true; request: ResumeRequest } | Failure<AnfrageFehler>;

export async function requestResume(subjectId: string): Promise<AnfrageErgebnis> {
  const answer = await request<ResumeRequest>(
    RESUME_BASE_URL,
    `/resumes/${subjectId}/requests`,
    { method: "POST" },
    "errors.requestNotSent"
  );
  if (answer.ok) return { ok: true, request: answer.value };
  return interpretError<AnfrageFehler>(
    answer.error,
    {
      0: { reason: "offline", titleKey: "errors.noConnection" },
      409: { reason: "already-asked", titleKey: "errors.alreadyAsked" },
      403: {
        reason: "no-company",
        titleKey: "errors.onlyCompaniesRequestResume",
        detailKey: "errors.chooseCompany",
      },
      // Fragen setzt die PROFIL-Freigabe voraus, nie die Existenz eines
      // Lebenslaufs: „hat schon einen geschrieben" ist eine Tatsache über die
      // Person, nach der niemand tasten können soll.
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
