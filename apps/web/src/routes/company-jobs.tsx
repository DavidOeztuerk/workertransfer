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
  listOwnJobs,
  publishJob,
} from "../jobs/client";

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
}

const EMPTY: Draft = {
  title: "",
  description: "",
  location: "",
  remote: "none",
  employment: "full_time",
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
    mutationFn: () => createJob(draft),
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
