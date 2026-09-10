import type { BerufsfeldWahl } from "../../../shared/lib/berufsfelder";

/**
 * Wer gerade handelt.
 *
 * `tenantId === null` heisst "handelt als Person", NICHT "fehlt" (ADR-0017).
 * Auf einem Transfermarkt ist die Person ohne Firma der Normalfall, und die
 * beiden Zustände zu vermischen wäre genau der Fehler, der später eine
 * Firmenansicht für jemanden zeigt, der keine hat.
 */
export interface Session {
  userId: string;
  email: string;
  tenantId: string | null;
  /**
   * Die Sprache, die am KONTO steht.
   *
   * Sie kommt mit, damit die Wahl den Browser überlebt: wer auf einem
   * englischen Rechner Deutsch gewählt hat, sieht nach dem Anmelden auf einem
   * anderen Gerät wieder Deutsch. Der lokale Speicher weiss davon nichts.
   */
  language: string;
  /**
   * Der Name, unter dem die Person auftritt.
   *
   * Für die Initialen im Kopf. Aus der Adresse liesse er sich nicht ableiten:
   * `max.werber@…` ergäbe „M" und nicht „MW".
   */
  displayName: string;
  /** Bürgerlicher Vorname, oder leer. Nie im Token, nie an die KI als Anschrift. */
  givenName: string;
  /** Bürgerlicher Nachname, oder leer. */
  familyName: string;
  /**
   * In welcher Arbeitswelt die Person steht, oder `null`.
   *
   * `null` heisst „nicht angegeben" und ist der Normalfall — es bekommt die
   * heutige, neutrale Ansicht. Daraus folgt ausschliesslich, was die Oberfläche
   * ANBIETET (ADR-0039): kein Recht, keine Sichtbarkeit, keine Aussage über die
   * Person. Der Server antwortet auf jede Route unverändert.
   */
  berufsfeld: BerufsfeldWahl;
}

/**
 * Was `GET /auth/session` über den Zustand sagt — unabhängig vom User-Objekt.
 *
 * `renewable` heisst: Access tot, Refresh-Cookie da. Die Oberfläche muss dann
 * `POST /auth/refresh` rufen, sonst wirkt die Sitzung nach 15 Minuten tot.
 */
export type SessionWireState = "active" | "renewable" | "anonymous";

/** Eine Firma, für die jemand handeln darf. */
export interface Membership {
  id: string;
  name: string;
  role: string;
}

/**
 * Der Sitzungszustand kennt DREI Fälle, nicht zwei.
 *
 * `unknown` ist der Zustand vor der ersten Antwort und darf nicht als
 * "abgemeldet" gezeichnet werden: sonst blitzt beim Laden jeder Seite kurz die
 * Anmeldeaufforderung auf, obwohl die Person angemeldet ist.
 */
export type SessionStatus = "unknown" | "authenticated" | "anonymous";
