import { useState } from "react";
import { Alert, Button, Field } from "@workertransfer/ui";

import { type RegisterInput, registerUser, resendVerification } from "../auth/client";
import { AuthLayout } from "./auth-layout";
import { ZurueckHinweis } from "../jobs/ZurueckHinweis";

const CLAIM = "Dein Profil gehört dir.";
const SUPPORT =
  "Registrieren kostet nichts und verpflichtet zu nichts. Sichtbar wirst du erst, wenn du es willst.";

export function RegisterRoute() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState(false);
  const [resent, setResent] = useState(false);
  const [resending, setResending] = useState(false);
  const [resendFailed, setResendFailed] = useState(false);

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
      <AuthLayout
        title="Fast geschafft"
        claim={CLAIM}
        support={SUPPORT}
        // Dieselbe Nachricht, ob die Adresse neu war oder schon existierte — der
        // Server verrät es nicht, und beides ist wahr: es wurde eine E-Mail
        // geschickt.
        lead="Wir haben dir eine E-Mail geschickt. Bestätige darüber deine Adresse, dann kannst du dich anmelden."
        note={
          <>
            Adresse schon bestätigt? <a href="/login">Zur Anmeldung</a>
          </>
        }
      >
        {/* Vorher: `await resendVerification(email); setResent(true)` ohne
            try/catch und ohne „läuft". Bei einem Netzfehler wirft fetch, die
            Zusage wird nie gesetzt, und der Knopf tut sichtbar NICHTS — ein
            Erfolgszustand, den niemand erreicht, und ein Fehler, den niemand
            sieht.

            Der Server bleibt weiterhin stumm (202, egal ob etwas gesendet
            wurde) und darf es sein: das ist der Schutz gegen Enumeration. Nur
            der Transportfehler wird gemeldet, und der sagt nichts über
            Plattformmitgliedschaft. */}
        <Button
          variant="secondary"
          disabled={resending}
          onClick={async () => {
            setResending(true);
            setResendFailed(false);
            try {
              await resendVerification(email);
              setResent(true);
            } catch {
              setResendFailed(true);
            } finally {
              setResending(false);
            }
          }}
        >
          {resending ? "Wird gesendet…" : "E-Mail erneut senden"}
        </Button>
        {resendFailed ? (
          <Alert>
            Die E-Mail konnte gerade nicht angefordert werden. Versuch es später noch einmal.
          </Alert>
        ) : null}
        {resent ? (
          <Alert variant="notice">Falls nötig, ist die E-Mail erneut unterwegs.</Alert>
        ) : null}
      </AuthLayout>
    );
  }

  return (
    <AuthLayout
      title="Konto erstellen"
      claim={CLAIM}
      support={SUPPORT}
      note={
        <>
          Schon ein Konto? <a href="/login">Anmelden</a>
        </>
      }
    >
      {/* Kein Firmen- oder Mandantenfeld: registrieren ist der Akt einer Person
          (ADR-0017). Eine private Adresse ist der Normalfall — der
          Wechselwillige und der Arbeitssuchende brauchen kein Unternehmen. */}
      <ZurueckHinweis />
      <form onSubmit={onSubmit}>
        <Field
          label="E-Mail"
          type="email"
          autoComplete="username"
          hint="Privat oder geschäftlich. Für ein eigenes Unternehmen brauchst du später die Arbeitsadresse."
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
        />
        <Field
          label="Passwort"
          type="password"
          autoComplete="new-password"
          hint="Mindestens 12 Zeichen."
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />
        <Field
          label="Anzeigename"
          autoComplete="name"
          value={displayName}
          onChange={(e) => setDisplayName(e.target.value)}
          required
        />
        {error !== null ? <Alert>{error}</Alert> : null}
        <Button type="submit" disabled={busy}>
          {busy ? "Wird angelegt…" : "Registrieren"}
        </Button>
      </form>
    </AuthLayout>
  );
}
