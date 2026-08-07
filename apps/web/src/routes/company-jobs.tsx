import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button, Card, Field, TextArea } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import {
  type EmploymentType,
  type Job,
  type RemoteMode,
  closeJob,
  createJob,
  draftJobText,
  listOwnJobs,
  publishJob,
} from "../jobs/client";
import { parseSkills } from "../skills";

export interface CompanyJobsRouteProps {
  principal?: MeResponse | null;
}

const STATUS_LABEL: Record<Job["status"], string> = {
  draft: "Entwurf — sieht nur ihr",
  published: "Veröffentlicht",
  closed: "Geschlossen",
};

interface Draft {
  title: string;
  description: string;
  location: string;
  remote: RemoteMode;
  employment: EmploymentType;
  /** Als Zeile im Formular; zerlegt wird erst beim Abschicken. */
  skills: string;
}

const EMPTY: Draft = {
  title: "",
  description: "",
  location: "",
  remote: "none",
  employment: "full_time",
  skills: "",
};

export function CompanyJobsRoute({ principal = null }: CompanyJobsRouteProps) {
  const queryClient = useQueryClient();
  const tenantId = principal?.tenant_id ?? null;

  const jobsQuery = useQuery({
    queryKey: ["jobs", "own", tenantId],
    queryFn: listOwnJobs,
    enabled: tenantId !== null,
  });

  const [draft, setDraft] = useState<Draft>(EMPTY);
  const [error, setError] = useState<string | null>(null);

  function refresh() {
    void queryClient.invalidateQueries({ queryKey: ["jobs", "own", tenantId] });
  }

  const create = useMutation({
    mutationFn: () => createJob({ ...draft, skills: parseSkills(draft.skills) }),
    onSuccess: (result) => {
      if (result.ok) {
        setError(null);
        setDraft(EMPTY);
        refresh();
      } else {
        setError(result.message);
      }
    },
  });

  const transition = useMutation({
    mutationFn: ({ id, publish }: { id: string; publish: boolean }) =>
      publish ? publishJob(id) : closeJob(id),
    onSuccess: (result) => {
      setError(result.ok ? null : result.message);
      refresh();
    },
  });

  if (tenantId === null) {
    return (
      <main className="page page--narrow">
        <Card>
          <h1>Unsere Stellen</h1>
          <p>
            Wähle oben ein Unternehmen — oder <a href="/company/new">lege eines an</a>.
          </p>
        </Card>
      </main>
    );
  }

  const jobs = jobsQuery.data;

  return (
    <main className="page page--narrow">
      <header className="page__header">
        <h1>Unsere Stellen</h1>
        <p className="page__lead">
          Ein Entwurf sieht niemand außer euch. Veröffentlicht ist er für alle sichtbar, auch ohne
          Konto — und geschlossen bleibt geschlossen.
        </p>
      </header>

      {error !== null ? (
        <Card>
          <p className="auth__alert" role="alert">
            {error}
          </p>
        </Card>
      ) : null}

      <Card>
        <h2>Neue Stelle</h2>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate();
          }}
        >
          <Field
            label="Titel"
            value={draft.title}
            onChange={(e) => setDraft({ ...draft, title: e.target.value })}
            maxLength={160}
            required
          />
          <TextArea
            label="Beschreibung"
            rows={6}
            value={draft.description}
            onChange={(e) => setDraft({ ...draft, description: e.target.value })}
            maxLength={20000}
            required
          />
          <Field
            label="Ort"
            hint="Leer lassen, wenn es keinen festen gibt."
            value={draft.location}
            onChange={(e) => setDraft({ ...draft, location: e.target.value })}
            maxLength={160}
          />
          {/* Freiwillig. Eine erzwungene Liste wäre eine, die jemand ausfüllt,
              um das Formular loszuwerden — und Suchende glichen sich dann
              gegen Erfundenes ab. */}
          <Field
            label="Gesuchte Fähigkeiten"
            hint="Mit Komma getrennt, höchstens 20. Wer sich das ansieht, sieht, was ihm davon fehlt — eine Note vergibt niemand. Bekannte Schreibweisen vereinheitlichen wir, damit „Postgres“ und „PostgreSQL“ sich finden."
            value={draft.skills}
            onChange={(e) => setDraft({ ...draft, skills: e.target.value })}
          />
          <label className="wt-field">
            <span className="wt-field__label">Arbeitsform</span>
            <select
              className="wt-field__input"
              value={draft.remote}
              onChange={(e) => setDraft({ ...draft, remote: e.target.value as RemoteMode })}
            >
              <option value="none">Vor Ort</option>
              <option value="hybrid">Hybrid</option>
              <option value="full">Vollständig remote</option>
            </select>
          </label>
          <JobDraftHelp
            draft={draft}
            onDraft={(text) => setDraft((current) => ({ ...current, description: text }))}
          />
          {/* Angelegt wird immer ein Entwurf. Veröffentlichen ist ein zweiter,
              bewusster Schritt — ein Knopf, der beides täte, würde die Stelle
              draußen haben, bevor jemand sie gelesen hat. */}
          <Button type="submit" disabled={create.isPending}>
            {create.isPending ? "Wird angelegt…" : "Entwurf anlegen"}
          </Button>
        </form>
      </Card>

      <Card>
        <h2>Bestehende</h2>
        {jobs !== undefined && !jobs.ok ? (
          <p className="auth__alert" role="alert">
            {jobs.message}
          </p>
        ) : null}
        {jobs?.ok && jobs.jobs.length === 0 ? <p>Noch keine Stelle angelegt.</p> : null}
        {jobs?.ok && jobs.jobs.length > 0 ? (
          <ul className="team">
            {jobs.jobs.map((job) => (
              <li key={job.id}>
                <span>{job.title}</span>
                <span className="team__role">{STATUS_LABEL[job.status]}</span>
                {job.status === "draft" ? (
                  <Button
                    onClick={() => transition.mutate({ id: job.id, publish: true })}
                    disabled={transition.isPending}
                  >
                    Veröffentlichen
                  </Button>
                ) : null}
                {job.status !== "closed" ? (
                  <Button
                    variant="quiet"
                    onClick={() => transition.mutate({ id: job.id, publish: false })}
                    disabled={transition.isPending}
                  >
                    Schließen
                  </Button>
                ) : null}
              </li>
            ))}
          </ul>
        ) : null}
      </Card>
    </main>
  );
}

/**
 * Der Unternehmens-Agent — und der Unterschied zum Profil-Agenten ist der Punkt.
 *
 * Beide schreiben einen Entwurf auf Knopfdruck und speichern nichts. Aber der
 * hier arbeitet an einem Text, den das Unternehmen **selbst verfasst hat**, und
 * sagt über keine Person etwas. Das ist der Grund, warum genau dieser der
 * einzige Unternehmens-Agent aus dem ULTRAPLAN ist, der ohne eigene Abwägung
 * gebaut werden konnte: Scout, Candidate Ranking, Salary Recommendation und
 * Team Analyzer richten sich alle auf Menschen (ADR-0022/0024).
 *
 * Der Hinweis nennt deshalb auch nicht „deine Daten“, sondern was wirklich
 * hinausgeht: der Anzeigentext. Und er nennt, was der Entwurf nicht tun wird —
 * Anforderungen dazuerfinden. Wer die Regel kennt, prüft den Vorschlag darauf.
 */
function JobDraftHelp({ draft, onDraft }: { draft: Draft; onDraft: (text: string) => void }) {
  const [wish, setWish] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const hasText = draft.description.trim() !== "";

  const ask = useMutation({
    mutationFn: () =>
      draftJobText({
        title: draft.title,
        description: draft.description,
        location: draft.location,
        skills: parseSkills(draft.skills),
        wish,
      }),
    onSuccess: (result) => {
      if (result.ok) {
        setProblem(null);
        onDraft(result.draft);
      } else {
        setProblem(result.message);
      }
    },
  });

  return (
    <div className="draft-help">
      <Field
        label={hasText ? "Anzeige umformulieren lassen" : "Beim Schreiben helfen lassen"}
        hint="Optional: was euch wichtig ist („kürzer“, „weniger Floskeln“). Titel, Beschreibung, Ort und die gesuchten Fähigkeiten gehen dafür an Anthropic — nichts über Bewerbende. Anforderungen erfindet der Vorschlag keine dazu, und gespeichert wird er erst, wenn ihr den Entwurf anlegt."
        value={wish}
        onChange={(e) => setWish(e.target.value)}
        maxLength={200}
      />
      {problem !== null ? (
        <p className="auth__alert" role="alert">
          {problem}
        </p>
      ) : null}
      {/* type="button" ausgeschrieben, obwohl `Button` es ohnehin so vorgibt:
          dieser Knopf steht im selben <form> wie „Entwurf anlegen", und wer
          hier liest, soll nicht erst das UI-Paket aufschlagen müssen, um zu
          wissen, welcher der beiden absendet. Ein Test hält das Attribut fest —
          änderte sich die Vorgabe, legte dieser Knopf sonst die Stelle an. */}
      <Button type="button" variant="quiet" onClick={() => ask.mutate()} disabled={ask.isPending}>
        {ask.isPending
          ? "Wird geschrieben…"
          : hasText
            ? "Vorschlag holen (ersetzt die Beschreibung)"
            : "Vorschlag holen"}
      </Button>
    </div>
  );
}
