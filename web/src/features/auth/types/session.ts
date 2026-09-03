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
}

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
