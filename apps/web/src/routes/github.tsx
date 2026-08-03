import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button, Card, Field } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import {
  type GitHubConnection,
  connectGitHub,
  disconnectGitHub,
  getMyGitHub,
  refreshGitHub,
  verifyGitHub,
} from "../github/client";

export interface GitHubRouteProps {
  principal?: MeResponse | null;
}

/**
 * GitHub verbinden — bewiesen, nicht behauptet.
 *
 * Ein Feld „mein GitHub-Name" ohne Nachweis wäre eine Einladung, sich mit
 * fremder Arbeit zu schmücken, und das Opfer erführe es nie. Deshalb der Umweg
 * über einen öffentlichen Gist: dieselbe Form wie der Domain-Nachweis bei
 * Unternehmen (ADR-0019) — erst beweisen, dann behaupten.
 */
export function GitHubRoute({ principal = null }: GitHubRouteProps) {
  const queryClient = useQueryClient();
  const subjectId = principal?.user_id ?? null;
  const [login, setLogin] = useState("");
  const [error, setError] = useState<string | null>(null);

  const query = useQuery({
    queryKey: ["github", "me"],
    queryFn: getMyGitHub,
    enabled: subjectId !== null,
  });

  function applied(result: { ok: boolean; message?: string }) {
    setError(result.ok ? null : (result.message ?? "Das hat nicht geklappt."));
    void queryClient.invalidateQueries({ queryKey: ["github", "me"] });
  }

  const connect = useMutation({ mutationFn: () => connectGitHub(login), onSuccess: applied });
  const verify = useMutation({ mutationFn: verifyGitHub, onSuccess: applied });
  const refresh = useMutation({ mutationFn: refreshGitHub, onSuccess: applied });
  const disconnect = useMutation({
    mutationFn: disconnectGitHub,
    onSuccess: () => {
      setError(null);
      setLogin("");
      void queryClient.invalidateQueries({ queryKey: ["github", "me"] });
    },
  });

  if (subjectId === null) {
    return (
      <main className="page page--narrow">
        <Card>
          <h1>GitHub verbinden</h1>
          <p>
            Bitte <a href="/login">anmelden</a>, um dein GitHub-Konto zu verbinden.
          </p>
        </Card>
      </main>
    );
  }

  const connection: GitHubConnection | null | undefined = query.data;
  const busy = connect.isPending || verify.isPending || refresh.isPending;

  return (
    <main className="page page--narrow">
      <header className="page__header">
        <h1>GitHub verbinden</h1>
        <p className="page__lead">
          Was hier erscheint, sind <strong>Belege, keine Noten</strong>: deine öffentlichen
          Repositories mit Link. Diese Plattform rechnet daraus keine Punktzahl und keine
          Rangfolge — wer wissen will, ob dein Code gut ist, sieht ihn sich an.
        </p>
        <p className="requests__meta">
          Geholt wird nur, wenn du es auslöst. Es läuft kein Abgleich im Hintergrund: eine
          Plattform, die dir dauerhaft hinterhersieht, tut etwas anderes als eine, die einmal auf
          deine Bitte hinsieht.
        </p>
      </header>

      {error !== null ? (
        <p className="auth__alert" role="alert">
          {error}
        </p>
      ) : null}

      {query.isPending ? (
        <Card>
          <p role="status">Wird geladen…</p>
        </Card>
      ) : null}

      {connection === null || connection === undefined ? (
        <Card>
          <h2>Konto nennen</h2>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              connect.mutate();
            }}
          >
            <Field
              label="GitHub-Benutzername"
              hint="Nur der Name, ohne Adresse."
              placeholder="anna"
              value={login}
              onChange={(e) => setLogin(e.target.value)}
              maxLength={39}
              required
            />
            <Button type="submit" disabled={busy}>
              Weiter
            </Button>
          </form>
        </Card>
      ) : null}

      {connection != null && !connection.verified ? (
        <Card>
          <h2>Nachweis</h2>
          <p>
            Lege einen <strong>öffentlichen</strong> Gist an, dessen Beschreibung genau so lautet:
          </p>
          <pre className="github__challenge">{connection.challenge_description}</pre>
          <p className="requests__meta">
            Der Inhalt ist egal. Danach darf der Gist wieder weg — er beweist nur, dass du über
            das Konto <strong>{connection.login}</strong> verfügst.
          </p>
          <div className="transfer__actions">
            <Button onClick={() => verify.mutate()} disabled={busy}>
              {verify.isPending ? "Wird geprüft…" : "Nachweis prüfen"}
            </Button>
            <Button variant="quiet" onClick={() => disconnect.mutate()} disabled={busy}>
              Anderes Konto
            </Button>
          </div>
        </Card>
      ) : null}

      {connection != null && connection.verified ? (
        <Card>
          <h2>{connection.login}</h2>
          <p className="requests__meta">
            {connection.fetched_at !== null
              ? `Stand: ${new Date(connection.fetched_at).toLocaleString("de-DE")}`
              : "Noch nichts geholt."}{" "}
            · Sichtbar wird das erst, wenn du es unter{" "}
            <a href="/freigaben">Meine Freigaben</a> freigibst.
          </p>
          {connection.repositories.length === 0 ? (
            <p>Keine öffentlichen Repositories gefunden. Das ist kein Mangel — nur eine Auskunft.</p>
          ) : (
            <ul className="overview">
              {connection.repositories.map((repo) => (
                <li key={repo.name}>
                  <a href={repo.url} target="_blank" rel="noreferrer noopener">
                    {repo.name}
                  </a>
                  <span className="requests__meta">
                    {repo.language ?? "ohne Sprachangabe"} · {repo.stars} ★
                    {repo.description !== "" ? ` · ${repo.description}` : ""}
                  </span>
                </li>
              ))}
            </ul>
          )}
          <div className="transfer__actions">
            <Button variant="quiet" onClick={() => refresh.mutate()} disabled={busy}>
              {refresh.isPending ? "Wird geholt…" : "Aktualisieren"}
            </Button>
            <Button variant="quiet" onClick={() => disconnect.mutate()} disabled={busy}>
              Verbindung trennen
            </Button>
          </div>
        </Card>
      ) : null}
    </main>
  );
}
