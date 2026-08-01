import { useState } from "react";
import { Button, Card } from "@workertransfer/ui";

import { type MeResponse, createCompany, emailDomain, isPublicEmailDomain } from "../auth/client";

export interface CompanyNewRouteProps {
  // Injectable so the test can render a given principal without a live session.
  principal?: MeResponse | null;
}

export function CompanyNewRoute({ principal = null }: CompanyNewRouteProps) {
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [created, setCreated] = useState<string | null>(null);

  const email = principal?.email ?? "";
  const domain = emailDomain(email);

  if (email === "" || isPublicEmailDomain(email)) {
    return (
      <main>
        <section aria-labelledby="company-blocked-title">
          <Card>
            <h1 id="company-blocked-title">Unternehmen anlegen</h1>
            {/* Nur die Sichtbarkeit hängt hier an der Domain — die Ablehnung
                selbst spricht immer der Server aus (422). */}
            <p>
              Du bist mit einer privaten Adresse angemeldet. Ein Unternehmen kann nur anlegen, wer
              eine bestätigte Adresse auf dessen Domain nutzt — melde dich dafür mit deiner
              Arbeitsadresse an.
            </p>
          </Card>
        </section>
      </main>
    );
  }

  if (created !== null) {
    return (
      <main>
        <Card>
          <h1>{created} angelegt</h1>
          <p role="status">Du bist Administrator dieses Unternehmens.</p>
        </Card>
      </main>
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
    <main>
      <section aria-labelledby="company-new-title">
        <Card>
          <h1 id="company-new-title">Unternehmen anlegen</h1>
          {/* Kein Domain-Feld: die Domain wird aus der bestätigten Adresse
              abgeleitet. Was der Client nicht senden kann, kann er nicht
              fälschen (ADR-0017/0018). Angezeigt wird sie trotzdem. */}
          <p>
            Wird angelegt als <strong>{domain}</strong>
          </p>
          <form onSubmit={onSubmit}>
            <label>
              Name des Unternehmens
              <input value={name} onChange={(e) => setName(e.target.value)} required />
            </label>
            {error !== null ? <p role="alert">{error}</p> : null}
            <Button type="submit" disabled={busy}>
              {busy ? "Wird angelegt…" : "Unternehmen anlegen"}
            </Button>
          </form>
        </Card>
      </section>
    </main>
  );
}
