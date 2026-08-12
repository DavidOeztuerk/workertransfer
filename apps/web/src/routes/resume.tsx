import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import {
  Alert,
  Button,
  Card,
  Empty,
  Field,
  Fieldset,
  Loading,
  Page,
  Row,
  RowList,
} from "@workertransfer/ui";

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
      <Page title="Mein Lebenslauf" narrow>
        <Card>
          <p>
            Bitte <a href="/login">anmelden</a>, um deinen Lebenslauf zu bearbeiten.
          </p>
        </Card>
      </Page>
    );
  }

  if (resumeQuery.isPending) {
    return (
      <Page title="Mein Lebenslauf" narrow>
        <Card>
          <Loading label="Lebenslauf wird geladen…" />
        </Card>
      </Page>
    );
  }

  function update(index: number, patch: Partial<PositionInput>) {
    setSaved(false);
    setRows((current) => current.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  }

  const requests = requestsQuery.data;

  return (
    <Page
      title="Mein Lebenslauf"
      narrow
      lead="Diesen Lebenslauf sieht niemand, bis du ihn einem Unternehmen freigibst — Unternehmen für Unternehmen, jedes einzeln. Eine Freigabe kannst du jederzeit zurückziehen; sie wirkt sofort."
    >
      <Card>
        <h2>Anfragen</h2>
        {/* Reihenfolge nach dem Muster: lädt, dann Fehler, dann leer, dann
            Inhalt. Der LADEZUSTAND fehlte — und er sah aus wie „bislang hat
            niemand gefragt". Das ist die beruhigendste falsche Antwort, die es
            hier gibt: wer eine Anfrage erwartet, hätte die Seite zugemacht. */}
        {requestsQuery.isPending ? <Loading label="Anfragen werden geladen…" /> : null}
        {requests !== undefined && !requests.ok ? <Alert>{requests.message}</Alert> : null}
        {requests?.ok && requests.requests.length === 0 ? (
          <Empty title="Bislang hat niemand nach deinem Lebenslauf gefragt." />
        ) : null}
        {requests?.ok && requests.requests.length > 0 ? (
          <RowList>
            {requests.requests.map((request) => (
              <RequestRow
                key={request.id}
                request={request}
                busy={answer.isPending || withdraw.isPending}
                onAnswer={(grant) => answer.mutate({ id: request.id, grant })}
                onWithdraw={() => withdraw.mutate(request.id)}
              />
            ))}
          </RowList>
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
            <Fieldset key={index} legend={`Station ${index + 1}`}>
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
            </Fieldset>
          ))}

          <Button
            type="button"
            variant="quiet"
            onClick={() => setRows((current) => [...current, { ...EMPTY_POSITION }])}
          >
            Station hinzufügen
          </Button>

          {error !== null ? <Alert>{error}</Alert> : null}
          {/* `notice` und nicht `error`: eine Bestätigung, die den Vorleser
              unterbricht, ist Lärm. */}
          {saved && error === null ? <Alert variant="notice">Lebenslauf gespeichert.</Alert> : null}

          <Button type="submit" disabled={save.isPending}>
            {save.isPending ? "Wird gespeichert…" : "Speichern"}
          </Button>
        </form>
      </Card>
    </Page>
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
    <Row
      title="Ein Unternehmen fragt nach deinem Lebenslauf"
      meta={
        isPending
          ? "Noch nicht beantwortet"
          : request.status === "DECLINED"
            ? "Abgelehnt — dieses Unternehmen kann nicht erneut fragen"
            : holdsAccess
              ? "Freigegeben — das Unternehmen sieht deinen Lebenslauf"
              : "Freigabe zurückgezogen"
      }
      // Keine Knöpfe, wenn es nichts zu tun gibt: ein Zurückziehen für eine
      // Freigabe, die es nicht mehr gibt, wäre eine Lüge über den Zustand —
      // deshalb `undefined` und nicht ein deaktivierter Knopf.
      actions={
        isPending || holdsAccess ? (
          <>
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
          </>
        ) : undefined
      }
    />
  );
}
