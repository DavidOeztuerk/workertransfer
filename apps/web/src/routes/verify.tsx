import { useEffect, useState } from "react";
import { Button, Field } from "@workertransfer/ui";

import { resendVerification, verifyEmail } from "../auth/client";
import { AuthLayout } from "./auth-layout";

type State =
  | { phase: "working" }
  | { phase: "done" }
  | { phase: "failed"; expired: boolean; message: string };

const CLAIM = "Ein Klick, dann gehört das Konto dir.";
const SUPPORT = "Die Bestätigung stellt sicher, dass niemand deine Adresse für sich benutzt.";

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
    void verifyEmail(token).then((result) => {
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
