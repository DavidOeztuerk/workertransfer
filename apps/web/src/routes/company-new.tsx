import { useState } from "react";
import { Button, Field } from "@workertransfer/ui";

import { type MeResponse, createCompany, emailDomain, isPublicEmailDomain } from "../auth/client";
import { AuthLayout } from "./auth-layout";

export interface CompanyNewRouteProps {
  // Injectable so the test can render a given principal without a live session.
  principal?: MeResponse | null;
}

const CLAIM = "Ein Unternehmen ist eine Domain, kein Formularfeld.";
const SUPPORT =
  "Wer eine bestätigte Adresse auf einer Domain hat, darf sie beanspruchen. Sonst niemand.";

export function CompanyNewRoute({ principal = null }: CompanyNewRouteProps) {
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [created, setCreated] = useState<string | null>(null);

  const email = principal?.email ?? "";
  const domain = emailDomain(email);

  if (email === "" || isPublicEmailDomain(email)) {
    return (
      <AuthLayout
        title="Unternehmen anlegen"
        claim={CLAIM}
        support={SUPPORT}
        // Nur die Sichtbarkeit hängt hier an der Domain — die Ablehnung selbst
        // spricht immer der Server aus (422).
        lead="Du bist mit einer privaten Adresse angemeldet. Ein Unternehmen kann nur anlegen, wer eine bestätigte Adresse auf dessen Domain nutzt — melde dich dafür mit deiner Arbeitsadresse an."
        note={<a href="/">Zurück zur Startseite</a>}
      >
        <></>
      </AuthLayout>
    );
  }

  if (created !== null) {
    return (
      <AuthLayout
        title={`${created} angelegt`}
        claim={CLAIM}
        support={SUPPORT}
        lead="Du bist Administrator dieses Unternehmens."
        note={<a href="/">Zur Startseite</a>}
      >
        <></>
      </AuthLayout>
    );
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    const result = await createCompany(name);
    setBusy(false);
    if (result.ok) {
      setCreated(result.company.name);
    } else {
      setError(result.message);
    }
  }

  return (
    <AuthLayout
      title="Unternehmen anlegen"
      claim={CLAIM}
      support={SUPPORT}
      // Kein Domain-Feld: die Domain wird aus der bestätigten Adresse
      // abgeleitet. Was der Client nicht senden kann, kann er nicht fälschen
      // (ADR-0017/0018). Angezeigt wird sie trotzdem.
      lead={
        <>
          Wird angelegt als <strong>{domain}</strong>
        </>
      }
    >
      <form onSubmit={onSubmit}>
        <Field
          label="Name des Unternehmens"
          autoComplete="organization"
          value={name}
          onChange={(e) => setName(e.target.value)}
          required
        />
        {error !== null ? (
          <p className="auth__alert" role="alert">
            {error}
          </p>
        ) : null}
        <Button type="submit" disabled={busy}>
          {busy ? "Wird angelegt…" : "Unternehmen anlegen"}
        </Button>
      </form>
    </AuthLayout>
  );
}
