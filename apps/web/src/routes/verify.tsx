import { useEffect, useState } from "react";
import { Button, Field } from "@workertransfer/ui";

import { type VerifyResult, resendVerification, verifyEmail } from "../auth/client";
import { AuthLayout } from "./auth-layout";

type State =
  | { phase: "working" }
  | { phase: "done" }
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

  useEffect(() => {
    const token = new URLSearchParams(window.location.search).get("token") ?? "";
    if (token === "") {
      setState({ phase: "failed", expired: false, message: "Es fehlt ein Bestätigungslink." });
      return;
    }
    void verifyOnce(token).then((result) => {
      setState(result.ok ? { phase: "done" } : { phase: "failed", ...result });
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
      <AuthLayout
        title="E-Mail bestätigt"
        claim={CLAIM}
        support={SUPPORT}
        lead="Dein Konto ist freigeschaltet."
        note={<a href="/login">Zur Anmeldung</a>}
      >
        <></>
      </AuthLayout>
    );
  }

  return (
    <AuthLayout title="Bestätigung fehlgeschlagen" claim={CLAIM} support={SUPPORT}>
      <p className="auth__alert" role="alert">
        {state.message}
      </p>
      {/* Nur bei abgelaufenem Link lohnt ein neuer — ein ungültiger wird auch
          beim zweiten Versuch nicht gültig. */}
      {state.expired ? (
        <form
          onSubmit={async (e) => {
            e.preventDefault();
            await resendVerification(email);
            setResent(true);
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
          <Button type="submit">Neuen Link senden</Button>
          {resent ? (
            <p className="auth__note" role="status">
              Falls nötig, ist die E-Mail unterwegs.
            </p>
          ) : null}
        </form>
      ) : null}
    </AuthLayout>
  );
}
