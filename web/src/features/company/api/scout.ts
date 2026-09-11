// Die Suche nach Menschen — scout-service, nicht mehr profile-service.
//
// <strong>`GET /scout/candidates`.</strong> Der Vorgänger war `GET /candidates`
// bei profile-service; er ist am 11.09.2026 mit dieser Datei gefallen
// (ADR-0036). Zwei Suchen nebeneinander wären zwei Wahrheiten gewesen.
//
// <strong>Die Häkchen und die Belege kommen VOM SERVER</strong>, nicht aus einer
// Rechnung hier. Das ist Entscheidung 3 des ADR und keine Geschmacksfrage: läge
// die Liste nur in der Oberfläche, rechnete der Browser sich aus den Rohdaten
// eine Zahl — und ADR-0022 wäre durch die Hintertür da.

import { request } from "../../../core/api/client";
import { SCOUT_BASE_URL } from "../../../env";
import { type Fehlschlag, deuten } from "../../../shared/api/fehler";

/** Ein Haken: ein gesuchtes Wort und ein Ja oder Nein. */
export interface Haken {
  word: string;
  /** Ob die Person dieses Wort selbst genannt hat. */
  named: boolean;
}

/**
 * Ein Beleg mit Herkunft und Link.
 *
 * `origin` ist `topic` oder `language`: ein Topic hat ein Mensch an das
 * Repository geschrieben, einen Sprachnamen hat GitHub erkannt. Ohne die
 * Angabe läse sich beides wie eine Aussage über die Person.
 */
export interface Beleg {
  word: string;
  origin: "topic" | "language";
  project: string;
  url: string;
}

/**
 * Was der Server über die Vollständigkeit der Belege sagt.
 *
 * Ein Wort und kein Satz — die Oberfläche formuliert in der Sprache der
 * lesenden Person (ADR-0031). `none_released` und `unavailable` sind
 * ausdrücklich verschieden: „hier steht nichts" ist kein Urteil, „wir wissen es
 * gerade nicht" ist überhaupt keine Aussage über die Person.
 */
export type Belegstand = "complete" | "none_released" | "partial" | "unavailable";

/**
 * Das Häkchen zur Entfernung (ADR-0041).
 *
 * `unsaid` ist KEIN Nein. Es heisst: die Person hat nichts gesagt, oder es
 * wurde keine Stelle gewählt, oder ein Ort ist dem Server unbekannt. Wer nichts
 * gesagt hat, bekommt kein Kreuz.
 */
export type Erreichbarkeit = "reachable" | "further" | "unsaid";

/** Ein Mensch, der gefunden wurde. Ohne Punktwert, ohne Rang, ohne Prozent. */
export interface Treffer {
  subject_id: string;
  headline: string;
  bio: string;
  location: string;
  remote_ok: boolean;
  /** Was die Person selbst genannt hat. */
  named: string[];
  /** Ein Haken je gesuchtem Wort — nie eine Summe darüber. */
  checks: Haken[];
  evidence: Beleg[];
  evidence_state: Belegstand;
  /** Drei Worte, nie eine Kilometerzahl — die reist gar nicht erst mit. */
  reach: Erreichbarkeit;
  /** Was die Person gesagt hat, oder `null`. */
  reach_commute: string | null;
  /** Was die Stelle verlangt, oder `null`. */
  reach_attendance: string | null;
}

export interface Suchfilter {
  /**
   * Mit ODER verknüpft, und das ist der Grund, warum die Häkchen etwas sagen:
   * unter UND erfüllte jeder Treffer alles Gesuchte, jeder Haken wäre gesetzt,
   * und „welche Fähigkeit fehlt" hätte keine Antwort.
   */
  skills: string[];
  location: string;
  /** Nur `true` filtert. Es gibt kein „nur ohne Remote" — siehe Server. */
  remoteOnly: boolean;
}

export const OHNE_FILTER: Suchfilter = { skills: [], location: "", remoteOnly: false };

export type ScoutFehler = "no-company" | "unauthenticated" | "consent-unavailable" | "offline";

export type Trefferseite =
  | { ok: true; items: Treffer[]; nextCursor: string | null }
  | Fehlschlag<ScoutFehler>;

/**
 * Die Filter wandern in die URL, nicht in den Cursor.
 *
 * Ein Cursor, der Suchbedingungen einpackt, ist ein zweiter Ort, an dem die
 * Abfrage steht — und beim ersten Mal, wenn beide auseinanderlaufen, blättert
 * jemand still durch die falsche Menge.
 */
export function scoutQuery(
  cursor?: string,
  filter: Suchfilter = OHNE_FILTER,
  stelle?: string
): string {
  const params = new URLSearchParams();
  if (cursor !== undefined && cursor !== "") params.set("cursor", cursor);
  // Die Stelle FILTERT NICHT (ADR-0041): sie fuegt jedem Treffer eine Auskunft
  // hinzu und nimmt keinen weg. Sie steht deshalb neben den Filtern und nicht
  // unter ihnen.
  if (stelle !== undefined && stelle !== "") params.set("stelle", stelle);
  for (const skill of filter.skills) {
    const trimmed = skill.trim();
    if (trimmed !== "") params.append("skill", trimmed);
  }
  if (filter.location.trim() !== "") params.set("location", filter.location.trim());
  // `remote=false` wird gar nicht erst gesendet: es wäre kein Filter, sondern
  // Rauschen in der URL.
  if (filter.remoteOnly) params.set("remote", "true");
  const query = params.toString();
  return query === "" ? "" : `?${query}`;
}

/**
 * Eine Seite Treffer — nur für Unternehmen.
 *
 * <strong>`next` und nicht `next_cursor`.</strong> Der Vorgänger las
 * `next_cursor`, der Server schrieb `next`: der Zeiger war damit IMMER `null`,
 * der Knopf „mehr laden" erschien nie, und die Kandidatenliste hatte keinen Weg
 * auf Seite 2. Gemessen und in `SCOUT-UND-BERATER-BESTAND.md` als offene Lücke
 * notiert — die Ursache war ein Feldname.
 *
 * Die Gründe sind unterscheidbar, weil die Seite auf jeden davon anders
 * antwortet: „kein Unternehmen aktiv" ist behebbar (umschalten), „Ledger
 * schweigt" ist es nicht, und eine leere Seite ist überhaupt kein Fehler.
 */
export async function sucheKandidaten(
  cursor?: string,
  filter: Suchfilter = OHNE_FILTER,
  signal?: AbortSignal,
  stelle?: string
): Promise<Trefferseite> {
  const answer = await request<{ items?: Treffer[]; next?: string | null }>(
    SCOUT_BASE_URL,
    `/scout/candidates${scoutQuery(cursor, filter, stelle)}`,
    { signal },
    "fehler.listeNichtGeladen2"
  );
  if (answer.ok) {
    return {
      ok: true,
      items: answer.value?.items ?? [],
      nextCursor: answer.value?.next ?? null,
    };
  }
  return deuten<ScoutFehler>(
    answer.error,
    {
      0: { reason: "offline", titel: "fehler.keineVerbindung" },
      401: {
        reason: "unauthenticated",
        titel: "fehler.sitzungAbgelaufen",
        text: "fehler.erneutAnmelden",
      },
      403: {
        reason: "no-company",
        titel: "fehler.nurFirmenProfile",
        text: "fehler.firmaWaehlen",
      },
      // NICHT als leere Liste zeigen: das wäre die Behauptung, niemand habe
      // freigegeben — und genau das weiß in diesem Moment niemand.
      503: {
        reason: "consent-unavailable",
        titel: "fehler.ledgerSchweigt",
        text: "fehler.lieberNichts",
      },
    },
    "offline"
  );
}
