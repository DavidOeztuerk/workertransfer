import { useMemo, useState } from "react";
import { Alert, Button, Field, RadioGroup } from "@workertransfer/ui";

import {
  type RegisterInput,
  isPublicEmailDomain,
  registerUser,
  resendVerification,
} from "../auth/client";
import { AuthLayout } from "./auth-layout";
import { ZurueckHinweis } from "../jobs/ZurueckHinweis";

const CLAIM = "Dein Profil gehört dir.";
const SUPPORT =
  "Registrieren kostet nichts und verpflichtet zu nichts. Sichtbar wirst du erst, wenn du es willst.";

/** Vorauswahl aus der Adresse: die Hero-Knöpfe der Startseite tragen sie mit. */
function gewuenschteArt(): "person" | "company" {
  if (typeof window === "undefined") return "person";
  return new URLSearchParams(window.location.search).get("as") === "company"
    ? "company"
    : "person";
}

export function RegisterRoute() {
  // Die Voreinstellung ist "person", und zwar ausdrücklich: registrieren ist der
  // Akt einer natürlichen Person (ADR-0017), und der Normalfall auf einem
  // Transfermarkt ist jemand ohne Unternehmen.
  const [art, setArt] = useState<"person" | "company">(gewuenschteArt);
  const [companyName, setCompanyName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState(false);
  const [resent, setResent] = useState(false);
  const [resending, setResending] = useState(false);
  const [resendFailed, setResendFailed] = useState(false);

  // Nur wenn überhaupt eine Adresse dasteht: isPublicEmailDomain("") würde über
  // einen leeren Domainteil urteilen und beim Tippen des ersten Zeichens
  // aufblitzen.
  const freemail = useMemo(
    () => art === "company" && email.includes("@") && isPublicEmailDomain(email),
    [art, email]
  );

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    const input: RegisterInput =
      art === "company" ? { email, password, displayName, companyName } : { email, password, displayName };
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
      {/* Kein Mandantenfeld: der Client nennt hier höchstens einen NAMEN, nie
          eine Zugehörigkeit. Die Domain leitet der Server aus der bestätigten
          Adresse ab — was der Client nicht senden kann, kann er nicht fälschen
          (ADR-0017/0018/0019). */}
      <ZurueckHinweis />
      <form onSubmit={onSubmit}>
        <RadioGroup
          legend="Wofür registrierst du dich?"
          name="art"
          value={art}
          onChange={(next) => setArt(next === "company" ? "company" : "person")}
          options={[
            {
              value: "person",
              label: "Für mich",
              hint: "Du suchst oder bist wechselwillig. Sichtbar wirst du erst, wenn du es willst.",
            },
            {
              value: "company",
              label: "Für ein Unternehmen",
              hint: "Braucht deine Arbeitsadresse — daraus entsteht die Domain des Unternehmens.",
            },
          ]}
        />
        <Field
          label="E-Mail"
          type="email"
          autoComplete="username"
          hint={
            art === "company"
              ? "Deine Arbeitsadresse. Aus ihrer Domain entsteht das Unternehmen."
              : "Privat oder geschäftlich — beides ist in Ordnung."
          }
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
        />
        {art === "company" ? (
          <Field
            label="Name des Unternehmens"
            autoComplete="organization"
            hint="Entsteht mit der Bestätigung deiner Adresse, nicht sofort."
            value={companyName}
            onChange={(e) => setCompanyName(e.target.value)}
            required
          />
        ) : null}
        {/* Sofort, nicht erst nach der Bestätigungsmail: sonst erfährt jemand
            erst zwei Schritte später, dass sein Weg nicht geht. Die Absage
            spricht weiterhin der SERVER aus (422) — hier wird nur sichtbar
            gemacht, was ohnehin gilt. */}
        {freemail ? (
          <Alert>
            Ein Unternehmen braucht eine eigene Domain. Mit einer Adresse bei einem
            Massenanbieter geht das nicht — nimm deine Arbeitsadresse, oder registriere dich
            für dich selbst.
          </Alert>
        ) : null}
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
        <Button type="submit" disabled={busy || freemail}>
          {busy ? "Wird angelegt…" : "Registrieren"}
        </Button>
      </form>
    </AuthLayout>
  );
}
