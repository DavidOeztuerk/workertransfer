import { useAppSelector } from "../../../core/store/hooks";

/**
 * Wer gerade handelt — für die Seiten dieses Bereichs.
 *
 * <strong>Drei Zustände, nicht zwei.</strong> `unknown` heisst „noch nicht
 * gefragt" und muss als LADEN gezeichnet werden. Zeichnete man ihn als
 * „abgemeldet", blitzte auf jeder dieser Seiten kurz „Bitte anmelden" auf,
 * obwohl die Person angemeldet ist — und auf `/delete-account` wäre das der
 * schlimmste Satz, den die Anwendung sagen kann.
 */
export function usePerson(): {
  subjectId: string | null;
  email: string | null;
  /** Die Sitzung ist noch nicht geprüft. Weder angemeldet noch abgemeldet. */
  unbekannt: boolean;
} {
  const status = useAppSelector((zustand) => zustand.auth.status);
  const session = useAppSelector((zustand) => zustand.auth.session);

  return {
    subjectId: session?.userId ?? null,
    email: session?.email ?? null,
    unbekannt: status === "unknown",
  };
}
