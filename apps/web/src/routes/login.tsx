import { useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button, Field } from "@workertransfer/ui";

import { type LoginInput, login } from "../auth/client";
import { SESSION_QUERY_KEY } from "../auth/session";
import { AuthLayout } from "./auth-layout";

export function LoginRoute() {
  const queryClient = useQueryClient();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    const input: LoginInput = { email, password };
    const result = await login(input);
    setBusy(false);
    if (result.ok) {
      // The auth cookies are set now; drop the cached anonymous session so the
      // next read of GET /me reflects the login, then hand over to the app.
      await queryClient.invalidateQueries({ queryKey: SESSION_QUERY_KEY });
      window.location.href = "/";
    } else {
      setError(result.message);
    }
  }

  return (
    <AuthLayout
      title="Anmelden"
      claim="Wechseln ist eine Entscheidung, kein Zufall."
      support="Du bestimmst, wer dich sieht, wer dich anspricht und was du teilst."
      note={
        <>
          Noch kein Konto? <a href="/register">Jetzt registrieren</a>
        </>
      }
    >
      <form onSubmit={onSubmit}>
        <Field
          label="E-Mail"
          type="email"
          // autoComplete: ohne das kann kein Passwortmanager füllen — der
          // Browser mahnt es in der Konsole selbst an.
          autoComplete="username"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          required
        />
        <Field
          label="Passwort"
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
        />
        {error !== null ? (
          <p className="auth__alert" role="alert">
            {error}
          </p>
        ) : null}
        <Button type="submit" disabled={busy}>
          {busy ? "Anmeldung läuft…" : "Anmelden"}
        </Button>
      </form>
    </AuthLayout>
  );
}
