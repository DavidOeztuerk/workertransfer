import { useAppSelector } from "../../../core/store/hooks";
import type { Session, SessionStatus } from "../../auth/types/session";

/**
 * Wer gerade handelt — für eine Seite lesbar.
 *
 * <strong>Drei Zustände, nicht zwei.</strong> `status === "unknown"` heißt
 * „noch nicht gefragt" und muss als LADEZUSTAND gezeichnet werden. Zeichnet
 * eine Seite ihn als „abgemeldet", blitzt beim Kaltstart auf jeder Seite kurz
 * „Bitte anmelden" auf, obwohl die Person angemeldet ist. Die alten Seiten
 * kannten diesen Fall nicht: sie bekamen `principal` als Prop injiziert, und es
 * war entweder da oder `null`.
 *
 * `tenantId === null` heißt „handelt als Person", nicht „fehlt" (ADR-0017).
 * Auf einem Transfermarkt ist das der Normalfall.
 */
export interface Handelnder {
  status: SessionStatus;
  session: Session | null;
  /** Die Sitzung ist noch unbekannt — zeichne einen Ladezustand. */
  laedt: boolean;
  angemeldet: boolean;
  /** Die eigene Subject-ID, oder `null`. */
  subjectId: string | null;
  /** Das Unternehmen, für das gerade gehandelt wird, oder `null`. */
  tenantId: string | null;
  /** Handelt gerade für ein Unternehmen. */
  fuerFirma: boolean;
}

export function useHandelnder(): Handelnder {
  const status = useAppSelector((zustand) => zustand.auth.status);
  const session = useAppSelector((zustand) => zustand.auth.session);
  const angemeldet = status === "authenticated" && session !== null;

  return {
    status,
    session,
    laedt: status === "unknown",
    angemeldet,
    subjectId: angemeldet ? session.userId : null,
    tenantId: angemeldet ? session.tenantId : null,
    fuerFirma: angemeldet && session.tenantId !== null,
  };
}
