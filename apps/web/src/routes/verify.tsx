import { useEffect, useState } from "react";
import { Alert, Button, Field } from "@workertransfer/ui";

import { type VerifyResult, resendVerification, verifyEmail } from "../auth/client";
import { AuthLayout } from "./auth-layout";

type State =
  | { phase: "working" }
  | { phase: "done"; company?: string; companyError?: string }
  | { phase: "failed"; expired: boolean; message: string };

const CLAIM = "Ein Klick, dann gehört das Konto dir.";
const SUPPORT = "Die Bestätigung stellt sicher, dass niemand deine Adresse für sich benutzt.";

/**
 * Je Token genau ein Aufruf — modulweit, nicht je Aufbau der Komponente.
 *
 * Der Bestätigungstoken ist einmalig: der zweite Aufruf verbraucht ihn nicht,
 * er scheitert mit „ungültig". Und weil beide Antworten in denselben Zustand
 * schreiben, entscheidet die zuletzt eintreffende, was die Person sieht — im
 * schlechten Fall „Bestätigung fehlgeschlagen", während ihr Konto gerade
 * freigeschaltet wurde. Im E2E-Lauf waren das 2 von 120 Aufrufen.
 *
 * Ein `useRef` reichte dafür nicht: er stirbt mit der Komponente, und genau ein
 * zweiter Aufbau ist der Fall (HMR, StrictMode, Reload, ein neu einhängender
 * Router). Gemerkt wird deshalb die **Zusage**, nicht nur die Tatsache — so
 * bekommt der zweite Aufbau dasselbe Ergebnis wie der erste, statt gar keines.
 */
const attempts = new Map<string, Promise<VerifyResult>>();

function verifyOnce(token: string): Promise<VerifyResult> {
  const running = attempts.get(token);
  if (running !== undefined) return running;
  const started = verifyEmail(token);
  attempts.set(token, started);
  return started;
}

export function VerifyRoute() {
  const [state, setState] = useState<State>({ phase: "working" });
  const [email, setEmail] = useState("");
  const [resent, setResent] = useState(false);
  const [resending, setResending] = useState(false);
  const [resendFailed, setResendFailed] = useState(false);

  useEffect(() => {
    const token = new URLSearchParams(window.location.search).get("token") ?? "";
    if (token === "") {
      setState({ phase: "failed", expired: false, message: "Es fehlt ein Bestätigungslink." });
      return;
    }
    void verifyOnce(token).then((result) => {
      setState(result.ok ? { phase: "done", ...result } : { phase: "failed", ...result });
    });
  }, []);

  if (state.phase === "working") {
    return (
      <AuthLayout title="Wird bestätigt…" claim={CLAIM} support={SUPPORT}>
        <p className="auth__lead" role="status">
          Einen Moment bitte.
        </p>
      </AuthLayout>
    );
  }

  if (state.phase === "done") {
    return (
      // Die Überschrift ist für ALLE drei Erfolge dieselbe, und sie ist in allen
      // drei wahr: bestätigt ist die E-MAIL. Was aus dem Unternehmen wurde, ist
      // eine zweite Aussage darunter.
      //
      // Sie bleibt außerdem buchstabengetreu „E-Mail bestätigt", weil die
      // E2E-Hilfe genau darauf prüft (`exact: true`) — ein weiches Muster traf
      // sonst auch „Wird bestätigt…", und der Test war zufrieden, während die
      // Bestätigung noch lief.
      <AuthLayout
        title="E-Mail bestätigt"
        claim={CLAIM}
        support={SUPPORT}
        lead={
          state.company !== undefined
            ? `Dein Konto ist freigeschaltet, und ${state.company} ist angelegt — du bist dort Administrator.`
            : "Dein Konto ist freigeschaltet."
        }
        note={<a href="/login">Zur Anmeldung</a>}
      >
        {/* Der dritte Ausgang: Konto bestätigt, Unternehmen abgelehnt. Ohne
            diesen Zweig wäre die Seite grün, während die halbe Absicht verpufft
            ist — und niemand wüsste, warum später kein Unternehmen da ist.

            Es gibt keinen zweiten Versuch: der Token ist verbraucht, und einen
            Knopf „Unternehmen anlegen" gibt es nicht mehr. Der richtige Weg ist
            ohnehin ein anderer — wer eine Adresse auf dieser Domain hat, hat
            dort Kollegen. */}
        {state.companyError === "domain_already_claimed" ? (
          <Alert>
            Dein Konto ist da, das Unternehmen nicht: für deine Domain gibt es hier schon
            eines. Bitte jemanden aus deinem Unternehmen, dich einzuladen — dann handelst du
            unter demselben Dach.
          </Alert>
        ) : state.companyError !== undefined ? (
          <Alert>
            Dein Konto ist da, das Unternehmen konnte nicht angelegt werden. Bitte jemanden
            aus deinem Unternehmen, dich einzuladen.
          </Alert>
        ) : null}
      </AuthLayout>
    );
  }

  return (
    <AuthLayout title="Bestätigung fehlgeschlagen" claim={CLAIM} support={SUPPORT}>
      <Alert>{state.message}</Alert>
      {/* Nur bei abgelaufenem Link lohnt ein neuer — ein ungültiger wird auch
          beim zweiten Versuch nicht gültig. */}
      {state.expired ? (
        <form
          onSubmit={async (e) => {
            e.preventDefault();
            setResending(true);
            setResendFailed(false);
            try {
              await resendVerification(email);
              setResent(true);
            } catch {
              // Ohne diesen Zweig tat der Knopf bei einem Netzfehler sichtbar
              // nichts: `resendVerification` wirft, die Zusage wurde nie
              // gesetzt. Derselbe Fehler steckte in der Registrierung.
              setResendFailed(true);
            } finally {
              setResending(false);
            }
          }}
        >
          <Field
            label="E-Mail"
            type="email"
            autoComplete="username"
            hint="An diese Adresse schicken wir einen neuen Link."
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
          />
          <Button type="submit" disabled={resending}>
            {resending ? "Wird gesendet…" : "Neuen Link senden"}
          </Button>
          {resendFailed ? (
            <Alert>
              Die E-Mail konnte gerade nicht angefordert werden. Versuch es später noch
              einmal.
            </Alert>
          ) : null}
          {resent ? <Alert variant="notice">Falls nötig, ist die E-Mail unterwegs.</Alert> : null}
        </form>
      ) : null}
    </AuthLayout>
  );
}
