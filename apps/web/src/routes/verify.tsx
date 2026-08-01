import { useEffect, useState } from "react";
import { Button, Card } from "@workertransfer/ui";

import { resendVerification, verifyEmail } from "../auth/client";

type State =
  | { phase: "working" }
  | { phase: "done" }
  | { phase: "failed"; expired: boolean; message: string };

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
      <main>
        <Card>
          <p role="status">Wird bestätigt…</p>
        </Card>
      </main>
    );
  }

  if (state.phase === "done") {
    return (
      <main>
        <section aria-labelledby="verify-done-title">
          <Card>
            <h1 id="verify-done-title">E-Mail bestätigt</h1>
            <p>Du kannst dich jetzt anmelden.</p>
            <a href="/login">Zur Anmeldung</a>
          </Card>
        </section>
      </main>
    );
  }

  return (
    <main>
      <section aria-labelledby="verify-failed-title">
        <Card>
          <h1 id="verify-failed-title">Bestätigung fehlgeschlagen</h1>
          <p role="alert">{state.message}</p>
          {/* Nur bei abgelaufenem Link lohnt ein neuer — ein ungültiger wird
              auch beim zweiten Versuch nicht gültig. */}
          {state.expired ? (
            <form
              onSubmit={async (e) => {
                e.preventDefault();
                await resendVerification(email);
                setResent(true);
              }}
            >
              <label>
                E-Mail
                <input
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  required
                />
              </label>
              <Button type="submit">Neuen Link senden</Button>
              {resent ? <p role="status">Falls nötig, ist die E-Mail unterwegs.</p> : null}
            </form>
          ) : null}
        </Card>
      </section>
    </main>
  );
}
