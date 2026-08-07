// Die Löschung des eigenen Kontos beim identity-service (ADR-0027).
//
// **Kein Rumpf.** Kein `subject_id` — die Löschung gilt der Person, die fragt,
// und wer nichts angeben kann, kann nichts fälschen. Und kein `reason`: von
// einem Menschen, der sein Konto löschen will, eine Begründung zu verlangen,
// wäre ein Hebel gegen ihn.
//
// **202, nicht 200.** Der Aufruf sperrt das Konto und widerruft jede Sitzung
// sofort; die eigentliche Löschung läuft danach über die Outbox an neun
// Empfänger, in erzwungener Reihenfolge. „Angenommen" ist also die ehrliche
// Antwort — „fertig" wäre eine Behauptung über etwas, das gerade erst beginnt.
//
// Wie `LoginResult` eine unterscheidende Union statt einer Ausnahme: ein
// abgelehnter Aufruf ist ein Zustand, den die Seite zeigt.

import { API_BASE_URL } from "../env";

export type ErasureResult = { ok: true } | { ok: false; message: string };

export async function requestErasure(): Promise<ErasureResult> {
  let res: Response;
  try {
    res = await fetch(`${API_BASE_URL}/account/erasure`, {
      method: "POST",
      credentials: "include",
    });
  } catch {
    return { ok: false, message: "Keine Verbindung zum Server." };
  }

  if (res.status === 401) {
    // Nicht als allgemeiner Fehlschlag: wer hier abgelaufen ist, soll sich neu
    // anmelden können und nicht rätseln, ob gerade etwas gelöscht wurde.
    return { ok: false, message: "Deine Sitzung ist abgelaufen. Bitte melde dich erneut an." };
  }
  if (!res.ok) {
    return {
      ok: false,
      message: "Die Löschung konnte nicht angestoßen werden. Es wurde nichts gelöscht.",
    };
  }
  // `retained` steht in der Antwort und ist in der Voreinstellung leer
  // (ADR-0027 §3). Es wird hier bewusst NICHT ausgewertet: die Seite dürfte
  // sonst eine Ausnahme anzeigen, die es nicht gibt. Kommt der
  // Aufbewahrungsschalter je an, kommt die Anzeige mit dem Commit, der ihn
  // umlegt — zusammen mit der Begründung, die dann auch stimmt.
  return { ok: true };
}
