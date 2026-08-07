import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button, Card } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import {
  type PersonAction,
  type Transfer,
  listMyTransfers,
  personMove,
} from "../transfers/client";

export interface TransfersRouteProps {
  principal?: MeResponse | null;
}

export const RUNNING: Transfer["status"][] = ["interested", "talking", "offered", "accepted"];

export function euro(cents: number | null): string {
  if (cents === null) return "—";
  return new Intl.NumberFormat("de-DE", { style: "currency", currency: "EUR" }).format(cents / 100);
}

/**
 * Die Vorgänge aus Sicht der Person.
 *
 * Für jeden Übergang ein eigener Knopf, kein Auswahlfeld: wer sieht, was er tun
 * kann, muss nicht raten, was erlaubt ist. Und „Ablehnen" steht an jedem
 * laufenden Vorgang — ein Verfahren, aus dem man nicht aussteigen kann, ist
 * kein Verfahren, sondern eine Falle.
 */
export function TransfersRoute({ principal = null }: TransfersRouteProps) {
  const queryClient = useQueryClient();
  const subjectId = principal?.user_id ?? null;
  const [error, setError] = useState<string | null>(null);

  const query = useQuery({
    queryKey: ["transfers", "me"],
    queryFn: listMyTransfers,
    enabled: subjectId !== null,
  });

  const move = useMutation({
    mutationFn: ({ id, action }: { id: string; action: PersonAction }) => personMove(id, action),
    onSuccess: (result) => {
      setError(result.ok ? null : result.message);
      void queryClient.invalidateQueries({ queryKey: ["transfers", "me"] });
    },
  });

  if (subjectId === null) {
    return (
      <main className="page page--narrow">
        <Card>
          <h1>Meine Gespräche</h1>
          <p>
            Bitte <a href="/login">anmelden</a>, um deine Gespräche zu sehen.
          </p>
        </Card>
      </main>
    );
  }

  const data = query.data;

  return (
    <main className="page page--narrow">
      <header className="page__header">
        <h1>Meine Gespräche</h1>
        <p className="page__lead">
          Ein Unternehmen kann nur zugehen, wenn du ihm deinen{" "}
          <a href="/markt">Marktstatus freigegeben</a> hast und gerade ansprechbar bist. Ablehnen
          kannst du jederzeit, in jedem Schritt.
        </p>
      </header>

      {error !== null ? (
        <p className="auth__alert" role="alert">
          {error}
        </p>
      ) : null}

      {query.isPending ? (
        <Card>
          <p role="status">Gespräche werden geladen…</p>
        </Card>
      ) : null}

      {data !== undefined && !data.ok ? (
        <Card>
          <p className="auth__alert" role="alert">
            {data.message}
          </p>
        </Card>
      ) : null}

      {data?.ok && data.transfers.length === 0 ? (
        <Card>
          <p>Es läuft gerade kein Gespräch.</p>
        </Card>
      ) : null}

      {data?.ok
        ? data.transfers.map((transfer) => (
            <Card key={transfer.id}>
              <PersonTransfer
                transfer={transfer}
                busy={move.isPending}
                onMove={(action) => move.mutate({ id: transfer.id, action })}
              />
            </Card>
          ))
        : null}
    </main>
  );
}

function PersonTransfer({
  transfer,
  busy,
  onMove,
}: {
  transfer: Transfer;
  busy: boolean;
  onMove: (action: PersonAction) => void;
}) {
  const running = RUNNING.includes(transfer.status);
  const needsRelease = transfer.requires_release && !transfer.release_confirmed;

  return (
    <div className="transfer">
      <h2>{TITLES[transfer.status]}</h2>
      {transfer.message !== "" ? <p className="transfer__message">{transfer.message}</p> : null}

      {transfer.status === "offered" || transfer.status === "accepted" ? (
        <dl className="transfer__offer">
          <dt>Angebot</dt>
          <dd>{transfer.offer_note === "" ? "—" : transfer.offer_note}</dd>
          <dt>Start</dt>
          <dd>{transfer.offer_start_on ?? "—"}</dd>
          <dt>Ablöse</dt>
          <dd>{euro(transfer.offer_fee_cents)}</dd>
        </dl>
      ) : null}

      {transfer.status === "accepted" && needsRelease ? (
        <p className="transfer__note">
          Es fehlt noch, dass dein Arbeitgeber dich gehen lässt. Diese Plattform fragt ihn nicht —
          sie weiß nicht, wer er ist und soll es nicht wissen. Bestätige selbst, sobald es geklärt
          ist: <strong>damit ist der Transfer abgeschlossen.</strong> Der letzte Schritt gehört dir,
          weil nur du weißt, ob du gehen darfst.
        </p>
      ) : null}

      <div className="transfer__actions">
        {transfer.status === "interested" ? (
          <Button onClick={() => onMove("accept-talk")} disabled={busy}>
            Gespräch annehmen
          </Button>
        ) : null}
        {transfer.status === "offered" ? (
          <Button onClick={() => onMove("accept-offer")} disabled={busy}>
            Angebot annehmen
          </Button>
        ) : null}
        {transfer.status === "accepted" && needsRelease ? (
          <Button onClick={() => onMove("confirm-release")} disabled={busy}>
            Freigabe bestätigen und abschließen
          </Button>
        ) : null}
        {running ? (
          <Button variant="quiet" onClick={() => onMove("decline")} disabled={busy}>
            Ablehnen
          </Button>
        ) : null}
      </div>
    </div>
  );
}

const TITLES: Record<Transfer["status"], string> = {
  interested: "Ein Unternehmen hat Interesse",
  talking: "Ihr seid im Gespräch",
  offered: "Es liegt ein Angebot vor",
  accepted: "Du hast angenommen",
  completed: "Abgeschlossen",
  declined: "Von dir abgelehnt",
  withdrawn: "Vom Unternehmen zurückgezogen",
};
