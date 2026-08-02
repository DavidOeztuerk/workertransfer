import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Button, Card, Field } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import {
  type PositionInput,
  type Resume,
  type ResumeRequest,
  answerRequest,
  getMyResume,
  listMyRequests,
  revokeAccess,
  saveMyResume,
} from "../resume/client";

export interface ResumeRouteProps {
  principal?: MeResponse | null;
}

const EMPTY_POSITION: PositionInput = {
  employer: "",
  title: "",
  started_on: "",
  ended_on: null,
  description: "",
};

function toRows(resume: Resume | null | undefined): PositionInput[] {
  return resume === null || resume === undefined ? [] : resume.positions.map((p) => ({ ...p }));
}

/** Leer heißt „läuft noch" — der Vertrag kennt dafür `null`, nicht "". */
function normalizeEnd(value: string): string | null {
  const trimmed = value.trim();
  return trimmed === "" ? null : trimmed;
}

export function ResumeRoute({ principal = null }: ResumeRouteProps) {
  const queryClient = useQueryClient();
  const subjectId = principal?.user_id ?? null;

  const resumeQuery = useQuery({
    queryKey: ["resume", "me"],
    queryFn: getMyResume,
    enabled: subjectId !== null,
  });
  const requestsQuery = useQuery({
    queryKey: ["resume", "requests", "me"],
    queryFn: listMyRequests,
    enabled: subjectId !== null,
  });

  const [rows, setRows] = useState<PositionInput[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  const loaded = resumeQuery.data;
  useEffect(() => {
    if (loaded !== undefined) setRows(toRows(loaded));
  }, [loaded]);

  const save = useMutation({
    mutationFn: () =>
      saveMyResume({
        positions: rows.map((row) => ({ ...row, ended_on: normalizeEnd(row.ended_on ?? "") })),
        education: [],
      }),
    onSuccess: (result) => {
      if (result.ok) {
        setError(null);
        setSaved(true);
        queryClient.setQueryData(["resume", "me"], result.resume);
      } else {
        setSaved(false);
        setError(result.message);
      }
    },
  });

  const answer = useMutation({
    mutationFn: ({ id, grant }: { id: string; grant: boolean }) => answerRequest(id, grant),
    onSuccess: (result) => {
      if (!result.ok) setError(result.message);
      else setError(null);
      void queryClient.invalidateQueries({ queryKey: ["resume", "requests", "me"] });
    },
  });

  const withdraw = useMutation({
    mutationFn: (id: string) => revokeAccess(id),
    onSuccess: (result) => {
      if (!result.ok) setError(result.message);
      else setError(null);
      void queryClient.invalidateQueries({ queryKey: ["resume", "requests", "me"] });
    },
  });

  if (subjectId === null) {
    return (
      <main className="page page--narrow">
        <Card>
          <h1>Mein Lebenslauf</h1>
          <p>
            Bitte <a href="/login">anmelden</a>, um deinen Lebenslauf zu bearbeiten.
          </p>
        </Card>
      </main>
    );
  }

  if (resumeQuery.isPending) {
    return (
      <main className="page page--narrow">
        <Card>
          <p role="status">Lebenslauf wird geladen…</p>
        </Card>
      </main>
    );
  }

  function update(index: number, patch: Partial<PositionInput>) {
    setSaved(false);
    setRows((current) => current.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  }

  const requests = requestsQuery.data;

  return (
    <main className="page page--narrow">
      <header className="page__header">
        <h1>Mein Lebenslauf</h1>
        <p className="page__lead">
          Diesen Lebenslauf sieht niemand, bis du ihn einem Unternehmen freigibst — Unternehmen für
          Unternehmen, jedes einzeln. Eine Freigabe kannst du jederzeit zurückziehen; sie wirkt
          sofort.
        </p>
      </header>

      <Card>
        <h2>Anfragen</h2>
        {requests !== undefined && !requests.ok ? (
          <p className="auth__alert" role="alert">
            {requests.message}
          </p>
        ) : null}
        {requests?.ok && requests.requests.length === 0 ? (
          <p>Bislang hat niemand nach deinem Lebenslauf gefragt.</p>
        ) : null}
        {requests?.ok && requests.requests.length > 0 ? (
          <ul className="requests">
            {requests.requests.map((request) => (
              <li key={request.id}>
                <RequestRow
                  request={request}
                  busy={answer.isPending || withdraw.isPending}
                  onAnswer={(grant) => answer.mutate({ id: request.id, grant })}
                  onWithdraw={() => withdraw.mutate(request.id)}
                />
              </li>
            ))}
          </ul>
        ) : null}
      </Card>

      <Card>
        <h2>Stationen</h2>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate();
          }}
        >
          {rows.map((row, index) => (
            // Der Index als Schlüssel ist hier richtig: die Zeilen haben keine
            // eigene Identität, die Reihenfolge macht erst der Server.
            <fieldset key={index} className="resume__position">
              <legend>Station {index + 1}</legend>
              <Field
                label="Arbeitgeber"
                value={row.employer}
                onChange={(e) => update(index, { employer: e.target.value })}
                required
              />
              <Field
                label="Position"
                value={row.title}
                onChange={(e) => update(index, { title: e.target.value })}
                required
              />
              <Field
                label="Von"
                hint="Monat, zum Beispiel 2020-01"
                placeholder="2020-01"
                value={row.started_on}
                onChange={(e) => update(index, { started_on: e.target.value })}
                required
              />
              <Field
                label="Bis"
                hint="Leer lassen, wenn du noch dort bist."
                placeholder="2023-06"
                value={row.ended_on ?? ""}
                onChange={(e) => update(index, { ended_on: e.target.value })}
              />
              <Button
                type="button"
                variant="quiet"
                onClick={() => setRows((current) => current.filter((_, i) => i !== index))}
              >
                Station entfernen
              </Button>
            </fieldset>
          ))}

          <Button
            type="button"
            variant="quiet"
            onClick={() => setRows((current) => [...current, { ...EMPTY_POSITION }])}
          >
            Station hinzufügen
          </Button>

          {error !== null ? (
            <p className="auth__alert" role="alert">
              {error}
            </p>
          ) : null}
          {saved && error === null ? <p className="page__note">Lebenslauf gespeichert.</p> : null}

          <Button type="submit" disabled={save.isPending}>
            {save.isPending ? "Wird gespeichert…" : "Speichern"}
          </Button>
        </form>
      </Card>
    </main>
  );
}

function RequestRow({
  request,
  busy,
  onAnswer,
  onWithdraw,
}: {
  request: ResumeRequest;
  busy: boolean;
  onAnswer: (grant: boolean) => void;
  onWithdraw: () => void;
}) {
  // `status` sagt, was geschehen ist; `active` sagt, was gilt. Nur `active`
  // entscheidet, ob ein Zurückziehen angeboten wird — ein Knopf für eine
  // Freigabe, die es nicht mehr gibt, wäre eine Lüge über den Zustand.
  const isPending = request.status === "PENDING";
  const holdsAccess = request.status === "GRANTED" && request.active === true;

  return (
    <div className="requests__row">
      <div>
        <p className="requests__title">Ein Unternehmen fragt nach deinem Lebenslauf</p>
        <p className="requests__meta">
          {isPending
            ? "Noch nicht beantwortet"
            : request.status === "DECLINED"
              ? "Abgelehnt — dieses Unternehmen kann nicht erneut fragen"
              : holdsAccess
                ? "Freigegeben — das Unternehmen sieht deinen Lebenslauf"
                : "Freigabe zurückgezogen"}
        </p>
      </div>
      <div className="requests__actions">
        {isPending ? (
          <>
            <Button onClick={() => onAnswer(true)} disabled={busy}>
              Freigeben
            </Button>
            <Button variant="quiet" onClick={() => onAnswer(false)} disabled={busy}>
              Ablehnen
            </Button>
          </>
        ) : null}
        {holdsAccess ? (
          <Button variant="quiet" onClick={onWithdraw} disabled={busy}>
            Zurückziehen
          </Button>
        ) : null}
      </div>
    </div>
  );
}
