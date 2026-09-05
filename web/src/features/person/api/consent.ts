import { request } from "../../../core/api/client";
import type { ApiError } from "../../../core/store/thunkHelpers";
import { CONSENT_BASE_URL } from "../../../env";

/**
 * Der Consent-Ledger, wie die Oberfläche ihn benutzt.
 *
 * Eine Stelle für alle Capabilities: Profil und Portfolio beantworten
 * verschiedene Fragen, aber sie stellen sie über dieselben drei Endpunkte. Zwei
 * Kopien dieser Aufrufe würden sich irgendwann über das Format uneinig.
 *
 * <strong>Nie zwischengespeichert.</strong> Ein Widerruf muss beim nächsten
 * Lesen wirken (ADR-0013) — ein Cache wäre hier kein Geschwindigkeitsdetail,
 * sondern ein Regelbruch.
 */
export const PROFILE_VISIBILITY = "profile.visibility:public";
export const PORTFOLIO_VISIBILITY = "portfolio.visibility:public";
// Der Dienst prüft sie seit jeher (`IEinwilligungstor.Sichtbarkeit`), die
// Oberfläche kannte sie nur zum ANZEIGEN — erteilen konnte man sie nirgends.
export const GITHUB_VISIBILITY = "github.visibility:public";

export type ConsentResult = { ok: true; granted: boolean } | { ok: false; error: ApiError };

/**
 * Gilt diese Einwilligung gerade? `null` heisst: der Ledger hat nicht geantwortet.
 *
 * Hier stand einmal `false` für diesen Fall, mit einer Begründung, die weiter
 * gilt: ein Schalter, der versehentlich „freigegeben" behauptet, ist die
 * gefährlichere Lüge. Sie schloss aber nur die eine Hälfte. `false` heisst für
 * den Aufrufer „nicht freigegeben", und ein Schalter in dieser Stellung ist
 * <strong>bedienbar</strong>: der nächste Klick schickt ein `grant` für eine
 * Einwilligung, deren Zustand niemand kennt — und wer längst freigegeben hatte,
 * liest „nicht freigegeben" und hält den Widerruf für erledigt.
 *
 * `null` trennt beides: die Anzeige bleibt aus, der Schalter wird gesperrt, und
 * die Seite sagt, was los ist.
 */
export async function isGranted(
  subjectId: string,
  capability: string,
  signal?: AbortSignal
): Promise<boolean | null> {
  const answer = await request<{ granted?: unknown; deleted?: unknown }>(
    CONSENT_BASE_URL,
    "/consent/check",
    { method: "POST", body: { subject_id: subjectId, capability }, signal }
  );
  if (!answer.ok) return null;
  return answer.value?.granted === true && answer.value.deleted !== true;
}

/**
 * Erteilen oder zurückziehen.
 *
 * Der Widerruf trägt immer eine Begründung, weil der Vertrag sie verlangt: eine
 * Entziehung muss erklärbar sein, eine Erteilung nicht. Die Voreinstellung sagt
 * ehrlich, wo der Widerruf ausgelöst wurde, statt etwas zu erfinden.
 */
export async function setGranted(
  subjectId: string,
  capability: string,
  granted: boolean,
  withdrawalReason: string
): Promise<ConsentResult> {
  const answer = await request<{ granted?: unknown }>(
    CONSENT_BASE_URL,
    granted ? "/consent/grant" : "/consent/revoke",
    {
      method: "POST",
      body: granted
        ? { subject_id: subjectId, capability }
        : { subject_id: subjectId, capability, reason: withdrawalReason },
    },
    "fehler.freigabeNichtGeaendert"
  );
  if (!answer.ok) return { ok: false, error: answer.error };
  return { ok: true, granted: answer.value?.granted === true };
}

export interface GrantedConsent {
  capability: string;
  granted_at: string;
}

export type MyConsentsResult =
  | { ok: true; consents: GrantedConsent[] }
  | { ok: false; error: ApiError };

/**
 * Was gerade gilt — nur die eigenen Freigaben.
 *
 * `GET /consent/me` nimmt <strong>keine</strong> Subjektkennung, weder im Pfad
 * noch in der Abfrage: eine fremde Liste würde sagen, welche anderen Unternehmen
 * Zugriff halten.
 *
 * Ein Fehler wird NICHT als leere Liste gezeigt. „Du hast nichts freigegeben"
 * ist eine Aussage, und auf genau dieser Seite wäre sie die beruhigendste
 * falsche Antwort, die das System geben kann.
 */
export async function listMyConsents(signal?: AbortSignal): Promise<MyConsentsResult> {
  const answer = await request<GrantedConsent[]>(
    CONSENT_BASE_URL,
    "/consent/me",
    { signal },
    "fehler.freigabenNichtGeladen"
  );
  if (!answer.ok) return { ok: false, error: answer.error };
  return { ok: true, consents: answer.value ?? [] };
}

export interface ParsedCapability {
  /** Der Bereich in Worten, oder `null`, wenn die Form unbekannt ist. */
  area: string | null;
  /** Die Tenant-UUID, wenn die Freigabe einem Unternehmen gilt. */
  tenantId: string | null;
  /** Gilt sie allen Unternehmen? */
  public: boolean;
}

/** Die bekannten Bereiche. Der Wert ist ein Katalogschlüssel, kein Satz. */
const AREAS: Record<string, string> = {
  profile: "freigaben.bereichProfile",
  resume: "freigaben.bereichResume",
  portfolio: "freigaben.bereichPortfolio",
  market: "freigaben.bereichMarket",
  github: "freigaben.bereichGithub",
};

/**
 * `resume.visibility:tenant:<uuid>` → lesbare Teile.
 *
 * Unbekannte Formen ergeben `area: null` — die Oberfläche zeigt sie dann roh
 * an, statt sie zu verschlucken. Eine Freigabe zu verbergen, weil ihr Format
 * nicht erkannt wurde, wäre auf dieser Seite der schlimmste denkbare Fehler.
 */
export function parseCapability(capability: string): ParsedCapability {
  const match = /^([a-z]+)\.visibility:(public|tenant:([0-9a-fA-F-]{36}))$/.exec(capability);
  if (match === null) return { area: null, tenantId: null, public: false };
  const area = AREAS[match[1] ?? ""] ?? null;
  return { area, tenantId: match[3] ?? null, public: match[2] === "public" };
}

export interface ConsentHistoryEntry {
  capability: string;
  action: "GRANT" | "REVOKE" | "DELETE";
  recorded_at: string;
  /** Nur der betroffenen Person gegenüber gefüllt. */
  reason: string | null;
}

export type ConsentHistoryResult =
  | { ok: true; events: ConsentHistoryEntry[] }
  | { ok: false; error: ApiError };

/** Die eigene Geschichte — für die Auskunft, nicht für die Übersicht. */
export async function listMyConsentHistory(
  signal?: AbortSignal
): Promise<ConsentHistoryResult> {
  const answer = await request<ConsentHistoryEntry[]>(
    CONSENT_BASE_URL,
    "/consent/me/history",
    { signal },
    "fehler.historieNichtGeladen"
  );
  if (!answer.ok) return { ok: false, error: answer.error };
  return { ok: true, events: answer.value ?? [] };
}
