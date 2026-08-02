import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Button, Card, Field } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import {
  type Availability,
  type MarketRequest,
  answerMarketRequest,
  getMyMarketStatus,
  listMyMarketRequests,
  revokeMarketAccess,
  saveMyMarketStatus,
} from "../market/client";

export interface MarketRouteProps {
  principal?: MeResponse | null;
}

const CHOICES: { value: Availability; label: string; hint: string }[] = [
  { value: "open", label: "Ich suche aktiv", hint: "Unternehmen mit Freigabe dürfen zugehen." },
  {
    value: "listening",
    label: "Ich höre zu",
    hint: "Ich suche nicht, bin aber für ein gutes Angebot ansprechbar.",
  },
  {
    value: "unavailable",
    label: "Gerade nicht",
    hint: "Auch mit Freigabe darf mich niemand ansprechen.",
  },
];

/**
 * Eine eigene Seite, nicht ein Feld im Profil.
 *
 * Die harmloseste Angabe (ein Aushang) und die gefährlichste (die
 * Wechselabsicht) gehören nicht in dasselbe Formular — sonst verwechselt sie
 * irgendwann jemand.
 */
export function MarketRoute({ principal = null }: MarketRouteProps) {
  const queryClient = useQueryClient();
  const subjectId = principal?.user_id ?? null;

  const statusQuery = useQuery({
    queryKey: ["market", "me"],
    queryFn: getMyMarketStatus,
    enabled: subjectId !== null,
  });
  const requestsQuery = useQuery({
    queryKey: ["market", "requests", "me"],
    queryFn: listMyMarketRequests,
    enabled: subjectId !== null,
  });

  const [availability, setAvailability] = useState<Availability>("unavailable");
  const [employed, setEmployed] = useState(false);
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  // Die Voreinstellung ist der geladene Status, und der ist ohne Angabe
  // `unavailable`. Die Seite denkt sich nichts aus.
  const loaded = statusQuery.data;
  useEffect(() => {
    if (loaded !== undefined) {
      setAvailability(loaded.availability);
      setEmployed(loaded.employed);
      setNote(loaded.note);
    }
  }, [loaded]);

  const save = useMutation({
    mutationFn: () => saveMyMarketStatus({ availability, employed, note }),
    onSuccess: (result) => {
      if (result.ok) {
        setError(null);
        setSaved(true);
        queryClient.setQueryData(["market", "me"], result.status);
      } else {
        setSaved(false);
        setError(result.message);
      }
    },
  });

  const answer = useMutation({
    mutationFn: ({ id, grant }: { id: string; grant: boolean }) => answerMarketRequest(id, grant),
    onSuccess: (result) => {
      setError(result.ok ? null : result.message);
      void queryClient.invalidateQueries({ queryKey: ["market", "requests", "me"] });
    },
  });

  const withdraw = useMutation({
    mutationFn: (id: string) => revokeMarketAccess(id),
    onSuccess: (result) => {
      setError(result.ok ? null : result.message);
      void queryClient.invalidateQueries({ queryKey: ["market", "requests", "me"] });
    },
  });

  if (subjectId === null) {
    return (
      <main className="page page--narrow">
        <Card>
          <h1>Mein Marktstatus</h1>
          <p>
            Bitte <a href="/login">anmelden</a>, um deinen Marktstatus zu setzen.
          </p>
        </Card>
      </main>
    );
  }

  if (statusQuery.isPending) {
    return (
      <main className="page page--narrow">
        <Card>
          <p role="status">Marktstatus wird geladen…</p>
        </Card>
      </main>
    );
  }

  const requests = requestsQuery.data;

  return (
    <main className="page page--narrow">
      <header className="page__header">
        <h1>Mein Marktstatus</h1>
        <p className="page__lead">
          Ob du ansprechbar bist, sieht nur, wem du es freigegeben hast — Unternehmen für
          Unternehmen, jedes einzeln. Es gibt hier bewusst kein „für alle": dass jemand wechseln
          will, ist die heikelste Angabe auf dieser Plattform.
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
          <p>Bislang hat niemand gefragt.</p>
        ) : null}
        {requests?.ok && requests.requests.length > 0 ? (
          <ul className="requests">
            {requests.requests.map((request) => (
              <li key={request.id}>
                <MarketRequestRow
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
        <h2>Bin ich ansprechbar?</h2>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate();
          }}
        >
          <fieldset className="market__choices">
            <legend>Status</legend>
            {CHOICES.map((choice) => (
              <label key={choice.value} className="market__choice">
                <input
                  type="radio"
                  name="availability"
                  value={choice.value}
                  checked={availability === choice.value}
                  onChange={() => {
                    setSaved(false);
                    setAvailability(choice.value);
                  }}
                />
                <span>
                  <strong>{choice.label}</strong>
                  <span className="market__hint">{choice.hint}</span>
                </span>
              </label>
            ))}
          </fieldset>

          <label className="market__choice">
            <input
              type="checkbox"
              checked={employed}
              onChange={(e) => {
                setSaved(false);
                setEmployed(e.target.checked);
              }}
            />
            <span>
              <strong>Ich arbeite gerade irgendwo</strong>
              <span className="market__hint">
                Dann braucht ein Wechsel eine Freigabe deines Arbeitgebers. Diese Plattform fragt
                ihn nicht — sie weiß nicht, wer er ist, und soll es nicht wissen. Du bestätigst
                selbst, wenn es soweit ist.
              </span>
            </span>
          </label>

          <Field
            label="Notiz"
            hint="Was du suchst, in eigenen Worten. Sieht nur, wer freigeschaltet ist."
            value={note}
            onChange={(e) => {
              setSaved(false);
              setNote(e.target.value);
            }}
            maxLength={500}
          />

          {error !== null ? (
            <p className="auth__alert" role="alert">
              {error}
            </p>
          ) : null}
          {saved && error === null ? <p className="page__note">Marktstatus gespeichert.</p> : null}

          <Button type="submit" disabled={save.isPending}>
            {save.isPending ? "Wird gespeichert…" : "Speichern"}
          </Button>
        </form>
      </Card>
    </main>
  );
}

function MarketRequestRow({
  request,
  busy,
  onAnswer,
  onWithdraw,
}: {
  request: MarketRequest;
  busy: boolean;
  onAnswer: (grant: boolean) => void;
  onWithdraw: () => void;
}) {
  // `status` sagt, was geschehen ist; `active` sagt, was gilt. Nur `active`
  // entscheidet, ob ein Zurückziehen angeboten wird.
  const isPending = request.status === "PENDING";
  const holdsAccess = request.status === "GRANTED" && request.active === true;

  return (
    <div className="requests__row">
      <div>
        <p className="requests__title">Ein Unternehmen möchte sehen, ob du ansprechbar bist</p>
        <p className="requests__meta">
          {isPending
            ? "Noch nicht beantwortet"
            : request.status === "DECLINED"
              ? "Abgelehnt — dieses Unternehmen kann nicht erneut fragen"
              : holdsAccess
                ? "Freigegeben — das Unternehmen sieht deinen Marktstatus"
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
