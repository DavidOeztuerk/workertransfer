import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Alert, Button, Card, Field, Loading, Page } from "@workertransfer/ui";

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
      <Page title="GitHub verbinden" narrow>
        <Card>
          <p>
            Bitte <a href="/login">anmelden</a>, um dein GitHub-Konto zu verbinden.
          </p>
        </Card>
      </Page>
    );
  }

  const connection: GitHubConnection | null | undefined = query.data;
  const busy = connect.isPending || verify.isPending || refresh.isPending;

  return (
    <Page
      title="GitHub verbinden"
      narrow
      lead={
        <>
          Was hier erscheint, sind <strong>Belege, keine Noten</strong>: deine öffentlichen
          Repositories mit Link. Diese Plattform rechnet daraus keine Punktzahl und keine
          Rangfolge — wer wissen will, ob dein Code gut ist, sieht ihn sich an.
        </>
      }
      note="Geholt wird nur, wenn du es auslöst. Es läuft kein Abgleich im Hintergrund: eine Plattform, die dir dauerhaft hinterhersieht, tut etwas anderes als eine, die einmal auf deine Bitte hinsieht."
    >
      {error !== null ? <Alert>{error}</Alert> : null}

      {query.isPending ? (
        <Card>
          <Loading label="Verbindung wird geladen…" />
        </Card>
      ) : null}

      {/* `!query.isPending` gehört dazu: solange die Abfrage läuft, ist
          `connection` `undefined`, und vorher stand „Wird geladen…" UND das
          Formular „Konto nennen" gleichzeitig auf der Seite. Wer schnell tippt,
          nannte ein Konto, bevor die Seite wusste, ob schon eines verbunden
          ist. */}
      {!query.isPending && (connection === null || connection === undefined) ? (
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
          <p className="wt-field__hint">
            Der Inhalt ist egal. Danach darf der Gist wieder weg — er beweist nur, dass du über
            das Konto <strong>{connection.login}</strong> verfügst.
          </p>
          {/* `.transfer__actions` ist die geteilte Knopfreihe (transfers,
              company-transfers, github) und hat eine Regel. Sie hier durch ein
              Interna von `Row` zu ersetzen wäre dasselbe Ausleihen in neuer
              Richtung — umbenannt wird sie in E3e, wo ihr Eigentümer umgestellt
              wird. */}
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
          <p className="wt-field__hint">
            {connection.fetched_at !== null
              ? `Stand: ${new Date(connection.fetched_at).toLocaleString("de-DE")}`
              : "Noch nichts geholt."}{" "}
            · Sichtbar wird das erst, wenn du es unter{" "}
            <a href="/consents">Meine Freigaben</a> freigibst.
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
                  <span className="wt-field__hint">
                    {repo.language ?? "ohne Sprachangabe"} · {repo.stars} ★
                    {repo.description !== "" ? ` · ${repo.description}` : ""}
                  </span>
                </li>
              ))}
            </ul>
          )}
          {/* `.transfer__actions` ist die geteilte Knopfreihe (transfers,
              company-transfers, github) und hat eine Regel. Sie hier durch ein
              Interna von `Row` zu ersetzen wäre dasselbe Ausleihen in neuer
              Richtung — umbenannt wird sie in E3e, wo ihr Eigentümer umgestellt
              wird. */}
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
    </Page>
  );
}
