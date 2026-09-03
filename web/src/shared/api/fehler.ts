import type { ApiError } from "../../core/store/thunkHelpers";
import { i18n } from "../../core/i18n/i18n";
import type { Katalog } from "../../core/i18n/kataloge/de";

/**
 * Aus einem Statuscode einen Grund und einen Satz machen — an EINER Stelle.
 *
 * Warum überhaupt ein „Grund" neben dem Statuscode: die Seiten antworten auf
 * `403`, `404` und `503` nicht mit derselben Gestalt. `503` heißt „der Ledger
 * schweigt" und darf niemals als leere Liste erscheinen — das wäre die
 * Behauptung, niemand habe freigegeben, und genau das weiß in dem Moment
 * niemand. `403` heißt „du handelst für keine Firma" und ist eine Aussage über
 * den AUFRUFER, also ein ruhiger Hinweis und keine Fehlermeldung. `404` bleibt
 * ununterscheidbar von „nicht freigegeben"; die Oberfläche bastelt daraus keine
 * Auskunft, die der Server gerade verweigert hat.
 *
 * Was NICHT verlorengeht: `status` und `correlationId` bleiben stehen. Die
 * Kennung ist der einzige Faden, an dem eine Beschwerde durch alle Dienste
 * zurückverfolgbar ist — ein eigener Satz darf sie nicht abschneiden.
 *
 * Steht kein Eintrag in der Tabelle, bleibt der Satz des Servers stehen: sein
 * Problemdokument weiß bei `409` und `422` mehr über den Vorgang als wir hier.
 *
 * <strong>Diese Datei lag zweimal vor</strong> — je einmal unter `work/api` und
 * `company/api`, wortgleich, und der Kommentar dort sagte selbst, sie gehöre
 * nach `shared/`. Hier ist sie.
 */

/**
 * Ein Schlüssel aus dem Fehlerbereich des Katalogs — kein Satz.
 *
 * Der Typ ist das eigentliche Werkzeug: i18next gibt einen unbekannten
 * Schlüssel unverändert zurück, ein liegengebliebener deutscher Satz sähe in
 * der Oberfläche also RICHTIG aus und wäre in keiner anderen Sprache
 * übersetzt. So ist er ein Übersetzungsfehler.
 */
export type Fehlerschluessel = `fehler.${keyof Katalog["fehler"] & string}`;

export interface Deutung<R extends string> {
  reason: R;
  titel: Fehlerschluessel;
  /** Fehlt er, trägt der Titel allein. */
  text?: Fehlerschluessel;
}

export interface Fehlschlag<R extends string> {
  ok: false;
  reason: R;
  error: ApiError;
}

export function deuten<R extends string>(
  error: ApiError,
  tabelle: Partial<Record<number, Deutung<R>>>,
  ersatzgrund: R
): Fehlschlag<R> {
  const deutung = tabelle[error.status];
  if (deutung === undefined) return { ok: false, reason: ersatzgrund, error };

  // Übersetzt wird HIER und nicht beim Aufbau der Tabelle: die Tabellen stehen
  // auf Modulebene, ihre Werte entstünden also beim Laden — lange bevor jemand
  // eine Sprache wählen konnte.
  const titel = i18n.t(deutung.titel);
  return {
    ok: false,
    reason: deutung.reason,
    error: {
      ...error,
      title: titel,
      detail: deutung.text === undefined ? titel : i18n.t(deutung.text),
    },
  };
}

/** Ein Netzfehler kommt als `status: 0` — dort gibt es kein Problemdokument. */
export const OFFLINE = 0;
