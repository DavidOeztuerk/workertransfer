import { useState } from "react";
import { Button, Card } from "@workertransfer/ui";

import { type LoginInput, login } from "../auth/client";

export function LoginRoute() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [tenantId, setTenantId] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    const input: LoginInput = { email, password, tenantId };
    const result = await login(input);
    setBusy(false);
    if (result.ok) {
      window.location.href = "/";
    } else {
      setError(result.message);
    }
  }

  return (
    <main>
      <section aria-labelledby="login-title">
        <Card>
          <h1 id="login-title">Anmelden</h1>
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
              Mandant-ID
              <input
                value={tenantId}
                onChange={(e) => setTenantId(e.target.value)}
                required
                placeholder="00000000-0000-0000-0000-000000000000"
              />
            </label>
            {error !== null ? <p role="alert">{error}</p> : null}
            <Button type="submit" disabled={busy}>
              {busy ? "Anmeldung läuft…" : "Anmelden"}
            </Button>
          </form>
        </Card>
      </section>
    </main>
  );
}
