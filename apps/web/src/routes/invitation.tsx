import { useEffect, useState } from "react";

import { type AcceptResult, type Membership, acceptInvitation } from "../auth/team";
import { AuthLayout } from "./auth-layout";

type State =
  | { phase: "working" }
  | { phase: "joined"; membership: Membership }
  | { phase: "failed"; needsAccount: boolean; message: string };

const CLAIM = "Ein Unternehmen entscheidet, wen es hereinlässt.";
const SUPPORT =
  "Die Einladung gilt genau für die Adresse, an die sie ging — ein weitergeleiteter Link öffnet nichts.";

/**
 * Je Token genau ein Aufruf — modulweit, nicht je Aufbau der Komponente.
 *
 * Wortgleich zur Bestätigungsseite, und aus demselben Grund: der
 * Einladungstoken ist **einmalig**. Der zweite Aufruf nimmt die Einladung nicht
 * noch einmal an, er scheitert. Und weil beide Antworten in denselben Zustand
 * schreiben, entscheidet die zuletzt eintreffende, was die Person sieht — im
 * schlechten Fall „Einladung nicht angenommen", während sie dem Unternehmen
 * gerade beigetreten ist.
 *
 * Diese Seite wurde beim Reparieren von `/verify` übersehen; aufgefallen ist
 * sie als **Wackler** im E2E-Lauf (einmal rot, beim zweiten Versuch grün) —
 * genau die Sorte Befund, die man wegzuklicken versucht ist.
 *
 * Ein `useRef` reichte nicht: er stirbt mit der Komponente, und ein zweiter
 * Aufbau ist genau der Fall. Gemerkt wird die **Zusage**, nicht nur die
 * Tatsache — so bekommt der zweite Aufbau dasselbe Ergebnis wie der erste,
 * statt gar keines.
 */
const attempts = new Map<string, Promise<AcceptResult>>();

function acceptOnce(token: string): Promise<AcceptResult> {
  const running = attempts.get(token);
  if (running !== undefined) return running;
  const started = acceptInvitation(token);
  attempts.set(token, started);
  return started;
}

export function InvitationRoute() {
  const [state, setState] = useState<State>({ phase: "working" });

  useEffect(() => {
    const token = new URLSearchParams(window.location.search).get("token") ?? "";
    if (token === "") {
      setState({
        phase: "failed",
        needsAccount: false,
        message: "Es fehlt ein Einladungslink.",
      });
      return;
    }
    void acceptOnce(token).then((result) => {
      if (result.ok) {
        setState({ phase: "joined", membership: result.membership });
      } else {
        setState({
          phase: "failed",
          // Der häufigste Fall: die Mail wird geöffnet, bevor es ein Konto
          // gibt. Dann ist nicht der Link kaputt — es fehlt die Anmeldung.
          needsAccount: result.reason === "unauthenticated",
          message: result.message,
        });
      }
    });
  }, []);

  if (state.phase === "working") {
    return (
      <AuthLayout title="Einladung wird geprüft…" claim={CLAIM} support={SUPPORT}>
        <p className="auth__lead" role="status">
          Einen Moment bitte.
        </p>
      </AuthLayout>
    );
  }

  if (state.phase === "joined") {
    return (
      <AuthLayout
        title={`Willkommen bei ${state.membership.name}`}
        claim={CLAIM}
        support={SUPPORT}
        lead={
          state.membership.role === "admin"
            ? "Du bist Administrator dieses Unternehmens."
            : "Du bist Mitglied dieses Unternehmens."
        }
        note={
          <>
            Um dafür zu handeln, wähle das Unternehmen oben aus. Wir wechseln nicht von selbst —
            sonst würdest du ungefragt aus dem Unternehmen herausgeholt, in dem du gerade
            arbeitest. <a href="/">Zur Startseite</a>
          </>
        }
      >
        <></>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout
      title="Einladung nicht angenommen"
      claim={CLAIM}
      support={SUPPORT}
      note={
        state.needsAccount ? (
          <>
            <a href="/login">Anmelden</a> — oder zuerst{" "}
            <a href="/register">Registrieren</a> und den Link danach erneut öffnen.
          </>
        ) : (
          <a href="/">Zur Startseite</a>
        )
      }
    >
      <p className="auth__alert" role="alert">
        {state.message}
      </p>
    </AuthLayout>
  );
}
