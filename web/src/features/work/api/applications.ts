// Client für applications-service.
 //
 // Die Bewerbung trägt keine Profildaten — nur eine `subject_id`. Wer Profil,
 // Lebenslauf oder Portfolio sehen will, fragt die zuständigen Dienste, und
 // dort greift der Consent-Ledger. Ein zweiter Weg an dieselben Daten hätte
 // einen zweiten Filter, und der weicht irgendwann vom ersten ab.
 
 import { request } from "../../../core/api/client";
 import { APPLICATIONS_BASE_URL } from "../../../env";
 import {
   type Deutung,
   type Fehlschlag,
   deuten,
 } from "../../../shared/api/fehler";
 
 export type ApplicationStatus = "submitted" | "reviewing" | "rejected" | "withdrawn" | "hired";
 
export interface ApplicantContact {
  full_name: string;
  line1: string;
  line2: string;
  postal_code: string;
  city: string;
  country: string;
  phone: string;
  email: string;
}

 export interface Application {
   id: string;
   job_id: string;
   tenant_id: string;
   subject_id: string;
   message: string;
   shares_resume: boolean;
   shares_portfolio: boolean;
   documents: string[];
   status: ApplicationStatus;
   created_at: string;
   updated_at: string;
   applicant_contact?: ApplicantContact;
 }
 
 /** Genau die vier Felder des Vertrags — und alle vier in snake_case. */
 export interface ApplicationInput {
   job_id: string;
   message: string;
   shares_resume: boolean;
   shares_portfolio: boolean;
 }
 
 export type BewerbungFehler =
   | "unauthenticated"
   | "gone"
   | "already"
   | "unavailable"
   | "invalid"
   | "offline";
 
 export type BewerbungErgebnis =
   | { ok: true; application: Application }
   | Fehlschlag<BewerbungFehler>;
 
 export type ListenErgebnis = { ok: true; applications: Application[] } | Fehlschlag<"fehlgeschlagen" | "offline">;
 
 export type DraftStatus = "generating" | "review" | "needs_changes" | "approved" | "sent" | "failed";
 
 export interface DraftComment {
   id: string;
   text: string;
   quote: string;
   resolved: boolean;
   created_at: string;
 }
 
 export interface Draft {
   id: string;
   job_id: string;
   subject: string;
   body: string;
   status: DraftStatus;
   version: number;
   error: string | null;
   writing_started_at: string | null;
   shares_resume: boolean;
   documents: string[];
   comments: DraftComment[];
   updated_at: string;
 }

 function alsEntwurf(roh: Draft): Draft {
   return {
     ...roh,
     subject: roh.subject ?? "",
     body: roh.body ?? "",
     shares_resume: roh.shares_resume === true,
     documents: Array.isArray(roh.documents) ? roh.documents : [],
     comments: Array.isArray(roh.comments) ? roh.comments : [],
     error: roh.error ?? null,
     writing_started_at: roh.writing_started_at ?? null,
   };
 }
 
 export type DraftFehler =
   | "unauthenticated"
   | "not_found"
   | "conflict"
   | "unavailable"
   | "invalid"
   | "offline";
 
 export type DraftErgebnis = { ok: true; draft: Draft } | Fehlschlag<DraftFehler>;
 export type DraftListenErgebnis = { ok: true; drafts: Draft[] } | Fehlschlag<"fehlgeschlagen" | "offline">;
 
 const SCHREIBFEHLER: Partial<
   Record<number, Deutung<BewerbungFehler>>
 > = {
   0: { reason: "offline", titel: "fehler.keineVerbindung" },
   401: {
     reason: "unauthenticated",
     titel: "fehler.kontoZumBewerben",
     text: "fehler.anmelden",
   },
   404: { reason: "gone", titel: "fehler.stelleGeschlossen" },
   // Weder abgelehnt noch angenommen: wir wissen es gerade nicht, und eine
   // Absage, die niemand ausgesprochen hat, wäre die schlimmere Antwort.
   503: {
     reason: "unavailable",
     titel: "fehler.dienstSchweigt",
     text: "fehler.spaeterErneut",
   },
 };
 
 const ENTWURF_FEHLER: Partial<
   Record<number, Deutung<DraftFehler>>
 > = {
   0: { reason: "offline", titel: "fehler.keineVerbindung" },
   401: {
     reason: "unauthenticated",
     titel: "fehler.kontoZumBewerben",
     text: "fehler.anmelden",
   },
   404: { reason: "not_found", titel: "fehler.entwurfNichtGefunden" },
   409: { reason: "conflict", titel: "fehler.schrittNichtErlaubt" },
   503: {
     reason: "unavailable",
     titel: "fehler.keinAnbieter",
     text: "fehler.entwurfAnbieterFehlt",
   },
 };
 
 async function schreiben(path: string, body?: unknown): Promise<BewerbungErgebnis> {
   const answer = await request<Application>(
     APPLICATIONS_BASE_URL,
     path,
     { method: "POST", body },
     "fehler.bewerbungNichtGesendet"
   );
   if (answer.ok) return { ok: true, application: answer.value };
   // `409` behält den Satz des Servers: der Zustand passt nicht, und die Person
   // soll nicht am Formular nach dem Fehler suchen.
   if (answer.error.status === 409) {
     return { ok: false, reason: "already", error: answer.error };
   }
   return deuten<BewerbungFehler>(answer.error, SCHREIBFEHLER, "invalid");
 }
 
 async function entwerfen(path: string, method: "POST" | "PATCH" | "PUT", body?: unknown): Promise<DraftErgebnis> {
   const answer = await request<Draft>(
     APPLICATIONS_BASE_URL,
     path,
     { method, body },
     "fehler.entwurfNichtGespeichert"
   );
   if (answer.ok) return { ok: true, draft: alsEntwurf(answer.value) };
   if (answer.error.status === 409) {
     return { ok: false, reason: "conflict", error: answer.error };
   }
   // 503 bleibt der Satz des Servers: „kein Anbieter" und „Modell nicht
   // geladen" sind beide 503, und die Katalogzeile für das Erste würde das
   // Zweite verschlucken.
   if (answer.error.status === 503) {
     return { ok: false, reason: "unavailable", error: answer.error };
   }
   return deuten<DraftFehler>(answer.error, ENTWURF_FEHLER, "invalid");
 }
 
 async function entwurfSchritt(path: string): Promise<DraftErgebnis> {
   const answer = await request<Draft>(
     APPLICATIONS_BASE_URL,
     path,
     { method: "POST" },
     "fehler.entwurfSchrittFehlgeschlagen"
   );
   if (answer.ok) return { ok: true, draft: alsEntwurf(answer.value) };
   if (answer.error.status === 409) {
     return { ok: false, reason: "conflict", error: answer.error };
   }
   if (answer.error.status === 503) {
     return { ok: false, reason: "unavailable", error: answer.error };
   }
   return deuten<DraftFehler>(answer.error, ENTWURF_FEHLER, "invalid");
 }
 
 export function apply(input: ApplicationInput): Promise<BewerbungErgebnis> {
   return schreiben("/applications", input);
 }
 
 export function withdrawApplication(applicationId: string): Promise<BewerbungErgebnis> {
   return schreiben(`/applications/${applicationId}/withdraw`);
 }
 
 export function advanceApplication(
   applicationId: string,
   status: "reviewing" | "rejected" | "hired"
 ): Promise<BewerbungErgebnis> {
   return schreiben(`/applications/${applicationId}/status`, { status });
 }
 
 async function list(path: string, signal?: AbortSignal): Promise<ListenErgebnis> {
   const answer = await request<Application[]>(
     APPLICATIONS_BASE_URL,
     path,
     { signal },
     "fehler.listeNichtGeladen"
   );
   if (answer.ok) return { ok: true, applications: answer.value ?? [] };
   // Kein Konto bzw. kein aktives Unternehmen: ein behebbarer Zustand, kein
   // Fehler, den man melden müsste.
   if (answer.error.status === 401 || answer.error.status === 403) {
     return { ok: true, applications: [] };
   }
   return deuten<"fehlgeschlagen" | "offline">(
     answer.error,
     { 0: { reason: "offline", titel: "fehler.keineVerbindung" } },
     "fehlgeschlagen"
   );
 }
 
 export function listMyApplications(signal?: AbortSignal): Promise<ListenErgebnis> {
   return list("/applications/me", signal);
 }
 
 export function listApplicationsForJob(
   jobId: string,
   signal?: AbortSignal
 ): Promise<ListenErgebnis> {
   return list(`/jobs/${jobId}/applications`, signal);
 }

 export async function getApplication(
   applicationId: string,
   signal?: AbortSignal
 ): Promise<BewerbungErgebnis> {
   const answer = await request<Application>(
     APPLICATIONS_BASE_URL,
     `/applications/${applicationId}`,
     { signal },
     "fehler.unternehmensbewerbungNichtGeladen"
   );
   if (answer.ok) return { ok: true, application: answer.value };
   return deuten<BewerbungFehler>(
     answer.error,
     {
       0: { reason: "offline", titel: "fehler.keineVerbindung" },
       401: {
         reason: "unauthenticated",
         titel: "fehler.kontoZumBewerben",
         text: "fehler.anmelden",
       },
       403: {
         reason: "gone",
         titel: "fehler.fuerFirmaHandelnNoetig",
         text: "fehler.firmaWaehlen",
       },
       404: { reason: "gone", titel: "fehler.ausschreibungFehlt" },
       503: {
         reason: "unavailable",
         titel: "fehler.dienstSchweigt",
         text: "fehler.spaeterErneut",
       },
     },
     "invalid"
   );
 }
 
 // Entwürfe
 export function createDrafts(jobIds: string[]): Promise<DraftListenErgebnis> {
   return request<Draft[]>(
     APPLICATIONS_BASE_URL,
     "/applications/drafts",
     { method: "POST", body: { job_ids: jobIds } },
     "fehler.entwuerfeNichtAngelegt"
   ).then((answer) =>
     answer.ok
       ? { ok: true, drafts: (answer.value ?? []).map(alsEntwurf) }
       : deuten<"fehlgeschlagen" | "offline">(
           answer.error,
           { 0: { reason: "offline", titel: "fehler.keineVerbindung" } },
           "fehlgeschlagen"
         )
   );
 }
 
 export function writeDraft(draftId: string): Promise<DraftErgebnis> {
   return entwurfSchritt(`/applications/drafts/${draftId}/write`);
 }
 
 export function getDrafts(signal?: AbortSignal): Promise<DraftListenErgebnis> {
   return request<Draft[]>(
     APPLICATIONS_BASE_URL,
     "/applications/drafts",
     { signal },
     "fehler.entwuerfeNichtGeladen"
   ).then((answer) =>
     answer.ok
       ? { ok: true, drafts: (answer.value ?? []).map(alsEntwurf) }
       : deuten<"fehlgeschlagen" | "offline">(
           answer.error,
           { 0: { reason: "offline", titel: "fehler.keineVerbindung" } },
           "fehlgeschlagen"
         )
   );
 }
 
 export function getDraft(draftId: string, signal?: AbortSignal): Promise<DraftErgebnis> {
   return request<Draft>(
     APPLICATIONS_BASE_URL,
     `/applications/drafts/${draftId}`,
     { signal },
     "fehler.entwurfNichtGeladen"
   ).then((answer) =>
     answer.ok
       ? { ok: true, draft: alsEntwurf(answer.value) }
       : deuten<DraftFehler>(answer.error, ENTWURF_FEHLER, "invalid")
   );
 }
 
 export function updateDraft(draftId: string, subject: string, body: string): Promise<DraftErgebnis> {
   return entwerfen(`/applications/drafts/${draftId}`, "PATCH", { subject, body });
 }
 
 export function chooseAttachments(
   draftId: string,
   sharesResume: boolean,
   documents: string[]
 ): Promise<DraftErgebnis> {
   return entwerfen(`/applications/drafts/${draftId}/attachments`, "PUT", {
     shares_resume: sharesResume,
     documents,
   });
 }
 
 export function addComment(draftId: string, text: string, quote: string): Promise<DraftErgebnis> {
   return entwerfen(`/applications/drafts/${draftId}/comments`, "POST", { text, quote });
 }
 
 export function reviseDraft(draftId: string): Promise<DraftErgebnis> {
   return entwurfSchritt(`/applications/drafts/${draftId}/revise`);
 }
 
 export function approveDraft(draftId: string): Promise<DraftErgebnis> {
   return entwurfSchritt(`/applications/drafts/${draftId}/approve`);
 }
 
 export function sendDraft(draftId: string): Promise<DraftErgebnis> {
   return entwurfSchritt(`/applications/drafts/${draftId}/send`);
 }
 
 export function deleteDraft(draftId: string): Promise<{ ok: true } | Fehlschlag<DraftFehler>> {
   return request<void>(
     APPLICATIONS_BASE_URL,
     `/applications/drafts/${draftId}`,
     { method: "DELETE" },
     "fehler.entwurfNichtGeloescht"
   ).then((answer) =>
     answer.ok
       ? { ok: true }
       : deuten<DraftFehler>(answer.error, ENTWURF_FEHLER, "invalid")
   );
 }
