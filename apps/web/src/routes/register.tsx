import { useState } from "react";
import { Button, Card } from "@workertransfer/ui";

import { type RegisterInput, registerUser, resendVerification } from "../auth/client";

export function RegisterRoute() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState(false);
  const [resent, setResent] = useState(false);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    const input: RegisterInput = { email, password, displayName };
    const result = await registerUser(input);
    setBusy(false);
    if (result.ok) {
      setSent(true);
    } else {
      setError(result.message);
    }
  }

  if (sent) {
    return (
      <main>
        <section aria-labelledby="register-sent-title">
          <Card>
            <h1 id="register-sent-title">Fast geschafft</h1>
            {/* Dieselbe Nachricht, ob die Adresse neu war oder schon existierte —
                der Server verrät es nicht, und beides ist wahr: es wurde eine
                E-Mail geschickt. */}
            <p>
              Wir haben dir eine E-Mail geschickt. Bitte bestätige darüber deine Adresse, dann
              kannst du dich anmelden.
            </p>
            <Button
              type="button"
              onClick={async () => {
                await resendVerification(email);
                setResent(true);
              }}
            >
              E-Mail erneut senden
            </Button>
            {resent ? <p role="status">Falls nötig, ist die E-Mail erneut unterwegs.</p> : null}
          </Card>
        </section>
      </main>
    );
  }

  return (
    <main>
      <section aria-labelledby="register-title">
        <Card>
          <h1 id="register-title">Konto erstellen</h1>
          {/* Kein Firmen- oder Mandantenfeld: registrieren ist der Akt einer
              Person (ADR-0017). Eine private Adresse ist der Normalfall — der
              Wechselwillige und der Arbeitssuchende brauchen kein Unternehmen. */}
          <form onSubmit={onSubmit}>
            <label>
              E-Mail
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
              />
            </label>
            <label>
              Passwort
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
              />
            </label>
            <label>
              Anzeigename
              <input
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                required
              />
            </label>
            {error !== null ? <p role="alert">{error}</p> : null}
            <Button type="submit" disabled={busy}>
              {busy ? "Wird angelegt…" : "Registrieren"}
            </Button>
          </form>
        </Card>
      </section>
    </main>
  );
}
