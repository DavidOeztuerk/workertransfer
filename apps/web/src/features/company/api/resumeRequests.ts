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
import { type Fehlschlag, deuten } from "./fehler";

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

export type AnfrageErgebnis = { ok: true; request: ResumeRequest } | Fehlschlag<AnfrageFehler>;

export async function requestResume(subjectId: string): Promise<AnfrageErgebnis> {
  const antwort = await request<ResumeRequest>(
    RESUME_BASE_URL,
    `/resumes/${subjectId}/requests`,
    { method: "POST" },
    "Die Anfrage konnte nicht gestellt werden."
  );
  if (antwort.ok) return { ok: true, request: antwort.value };
  return deuten<AnfrageFehler>(
    antwort.error,
    {
      0: { reason: "offline", title: "Keine Verbindung zum Server." },
      409: { reason: "already-asked", title: "Ihr habt diese Person bereits gefragt." },
      403: {
        reason: "no-company",
        title: "Lebensläufe fragen nur Unternehmen an.",
        detail: "Wechsle oben auf ein Unternehmen.",
      },
      // Fragen setzt die PROFIL-Freigabe voraus, nie die Existenz eines
      // Lebenslaufs: „hat schon einen geschrieben" ist eine Tatsache über die
      // Person, nach der niemand tasten können soll.
      404: { reason: "not-available", title: "Diese Person ist gerade nicht anfragbar." },
      503: {
        reason: "unavailable",
        title: "Der Consent-Ledger antwortet gerade nicht.",
        detail: "Bitte später erneut versuchen.",
      },
    },
    "offline"
  );
}
