import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import {
  Alert,
  Button,
  Card,
  Checkbox,
  Empty,
  Field,
  Loading,
  Page,
  RadioGroup,
} from "@workertransfer/ui";

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
  // Nur ein GELUNGENER Abruf füllt das Formular. `null` heißt „nicht abrufbar":
  // vorher stand dann ein Ersatzwert darin („nicht ansprechbar", leere Notiz),
  // und ein Speichern schrieb ihn — die Person hatte ihre Ansprechbarkeit
  // zurückgezogen und ihre Notiz gelöscht, ohne es zu wollen.
  const loaded = statusQuery.data;
  useEffect(() => {
    if (loaded !== undefined && loaded !== null) {
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
      <Page title="Mein Marktstatus" narrow>
        <Card>
          <p>
            Bitte <a href="/login">anmelden</a>, um deinen Marktstatus zu setzen.
          </p>
        </Card>
      </Page>
    );
  }

  if (statusQuery.isPending) {
    return (
      <Page title="Mein Marktstatus" narrow>
        <Card>
          <Loading label="Marktstatus wird geladen…" />
        </Card>
      </Page>
    );
  }

  // Kein Formular, wenn der Abruf scheiterte. Die alte Zusage — „der Ersatz
  // fällt nie zugunsten des Marktes aus" — gilt damit STRENGER als vorher: es
  // wird gar nichts mehr erfunden, in keine Richtung.
  if (statusQuery.data === null) {
    return (
      <Page title="Mein Marktstatus" narrow>
        <Card>
          <Alert>
            Dein Marktstatus ist gerade nicht abrufbar. Ändern lässt er sich erst wieder, wenn er
            lesbar ist — sonst würdest du womöglich zurücknehmen, was du nie zurückgenommen hast.
          </Alert>
        </Card>
      </Page>
    );
  }

  const requests = requestsQuery.data;

  return (
    <Page
      title="Mein Marktstatus"
      narrow
      lead={
        <>
          Ob du ansprechbar bist, sieht nur, wem du es freigegeben hast — Unternehmen für
          Unternehmen, jedes einzeln. Es gibt hier bewusst kein „für alle": dass jemand wechseln
          will, ist die heikelste Angabe auf dieser Plattform.
        </>
      }
    >

      <Card>
        <h2>Anfragen</h2>
        {requestsQuery.isPending ? <Loading label="Anfragen werden geladen…" /> : null}
        {requests !== undefined && !requests.ok ? <Alert>{requests.message}</Alert> : null}
        {requests?.ok && requests.requests.length === 0 ? (
          <Empty title="Bislang hat niemand gefragt." />
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
          {/* Native Radios mit gemeinsamem `name` — erst dadurch bewegen die
              Pfeiltasten den Fokus innerhalb der Gruppe. Das Bauteil tut genau
              das, was hier von Hand stand. */}
          <RadioGroup
            legend="Status"
            name="availability"
            value={availability}
            onChange={(next) => {
              setSaved(false);
              setAvailability(next as Availability);
            }}
            options={CHOICES.map((choice) => ({
              value: choice.value,
              label: choice.label,
              hint: choice.hint,
            }))}
          />

          {/* Ein Kästchen, kein Schalter: es gilt mit dem Absenden, und darunter
              steht ein Speichern-Knopf. */}
          <Checkbox
            label="Ich arbeite gerade irgendwo"
            hint="Dann braucht ein Wechsel eine Freigabe deines Arbeitgebers. Diese Plattform fragt ihn nicht — sie weiß nicht, wer er ist, und soll es nicht wissen. Du bestätigst selbst, wenn es soweit ist."
            checked={employed}
            onChange={(e) => {
              setSaved(false);
              setEmployed(e.target.checked);
            }}
          />

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

          {error !== null ? <Alert>{error}</Alert> : null}
          {saved && error === null ? (
            <Alert variant="notice">Marktstatus gespeichert.</Alert>
          ) : null}

          <Button type="submit" disabled={save.isPending}>
            {save.isPending ? "Wird gespeichert…" : "Speichern"}
          </Button>
        </form>
      </Card>
    </Page>
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
