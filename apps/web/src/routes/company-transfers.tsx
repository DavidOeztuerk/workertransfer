import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Alert, Button, Card, Empty, Field, Loading, Page } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import {
  type CompanyAction,
  type Transfer,
  companyMove,
  listCompanyTransfers,
  makeOffer,
} from "../transfers/client";
import { RUNNING, euro } from "./transfers";

export interface CompanyTransfersRouteProps {
  principal?: MeResponse | null;
}

/** Die Vorgänge aus Sicht des Unternehmens. */
export function CompanyTransfersRoute({ principal = null }: CompanyTransfersRouteProps) {
  const queryClient = useQueryClient();
  const hasCompany = principal?.tenant_id != null;
  const [error, setError] = useState<string | null>(null);

  const query = useQuery({
    queryKey: ["transfers", "company", principal?.tenant_id ?? null],
    queryFn: listCompanyTransfers,
    enabled: hasCompany,
  });

  const move = useMutation({
    mutationFn: ({ id, action }: { id: string; action: CompanyAction }) => companyMove(id, action),
    onSuccess: (result) => {
      setError(result.ok ? null : result.message);
      void queryClient.invalidateQueries({ queryKey: ["transfers", "company"] });
    },
  });

  const offer = useMutation({
    mutationFn: ({
      id,
      note,
      startOn,
      fee,
    }: {
      id: string;
      note: string;
      startOn: string;
      fee: string;
    }) =>
      makeOffer(id, {
        note,
        // Leer heißt „nicht genannt" — der Vertrag kennt dafür `null`, nicht "".
        start_on: startOn.trim() === "" ? null : startOn.trim(),
        fee_cents: fee.trim() === "" ? null : Math.round(Number(fee) * 100),
      }),
    onSuccess: (result) => {
      setError(result.ok ? null : result.message);
      void queryClient.invalidateQueries({ queryKey: ["transfers", "company"] });
    },
  });

  if (!hasCompany) {
    return (
      <Page title="Transfers" narrow>
        <Card>
          <p>
            Transfers führen nur Unternehmen. Wechsle oben auf ein Unternehmen — oder lass dich von
            jemandem aus deinem Unternehmen einladen.
          </p>
        </Card>
      </Page>
    );
  }

  const data = query.data;

  return (
    <Page
      title="Transfers"
      narrow
      lead="Die Ablöse wird hier festgehalten, nicht bewegt: diese Plattform führt kein Geld. Sie steht da, damit beide Seiten dieselbe Zahl im Blick haben."
    >
      {error !== null ? <Alert>{error}</Alert> : null}

      {/* Reihenfolge nach dem Muster: lädt, dann Fehler, dann leer, dann
          Inhalt. */}
      {query.isPending ? (
        <Card>
          <Loading label="Transfers werden geladen…" />
        </Card>
      ) : null}

      {data !== undefined && !data.ok ? <Alert>{data.message}</Alert> : null}

      {data?.ok && data.transfers.length === 0 ? (
        <Empty title="Es läuft gerade kein Transfer." />
      ) : null}

      {data?.ok
        ? data.transfers.map((transfer) => (
            <Card key={transfer.id}>
              <CompanyTransfer
                transfer={transfer}
                busy={move.isPending || offer.isPending}
                onMove={(action) => move.mutate({ id: transfer.id, action })}
                onOffer={(note, startOn, fee) =>
                  offer.mutate({ id: transfer.id, note, startOn, fee })
                }
              />
            </Card>
          ))
        : null}
    </Page>
  );
}

function CompanyTransfer({
  transfer,
  busy,
  onMove,
  onOffer,
}: {
  transfer: Transfer;
  busy: boolean;
  onMove: (action: CompanyAction) => void;
  onOffer: (note: string, startOn: string, fee: string) => void;
}) {
  const [note, setNote] = useState("");
  const [startOn, setStartOn] = useState("");
  const [fee, setFee] = useState("");

  const running = RUNNING.includes(transfer.status);
  // Abschließen darf das Unternehmen nur, wenn KEINE Freigabe nötig ist. Ist
  // eine nötig, schließt die Bestätigung der Person den Vorgang selbst ab —
  // der letzte Schritt gehört der Seite, die als einzige weiß, ob sie gehen
  // darf. Ein Zustand „freigegeben und noch offen" existiert nicht.
  const mayComplete = transfer.status === "accepted" && !transfer.requires_release;

  return (
    <div className="transfer">
      <h2>{TITLES[transfer.status]}</h2>
      <p className="wt-field__hint">
        {transfer.requires_release
          ? "Braucht eine Freigabe des aktuellen Arbeitgebers — die Person bestätigt sie selbst und schließt damit ab"
          : "Keine Freigabe nötig"}
      </p>

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

      {transfer.status === "talking" ? (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            onOffer(note, startOn, fee);
          }}
        >
          <Field
            label="Angebot"
            hint="Was ihr anbietet, in Worten."
            value={note}
            onChange={(e) => setNote(e.target.value)}
            maxLength={2000}
          />
          <Field
            label="Start"
            hint="Monat, zum Beispiel 2026-11."
            placeholder="2026-11"
            value={startOn}
            onChange={(e) => setStartOn(e.target.value)}
          />
          <Field
            label="Ablöse in Euro"
            hint="Wird festgehalten, nicht überwiesen."
            value={fee}
            onChange={(e) => setFee(e.target.value)}
          />
          <Button type="submit" disabled={busy}>
            Angebot machen
          </Button>
        </form>
      ) : null}

      <div className="transfer__actions">
        {mayComplete ? (
          <Button onClick={() => onMove("complete")} disabled={busy}>
            Abschließen
          </Button>
        ) : null}
        {running ? (
          <Button variant="quiet" onClick={() => onMove("withdraw")} disabled={busy}>
            Zurückziehen
          </Button>
        ) : null}
      </div>
    </div>
  );
}

const TITLES: Record<Transfer["status"], string> = {
  interested: "Interesse hinterlegt — die Person hat noch nicht geantwortet",
  talking: "Im Gespräch",
  offered: "Angebot abgegeben",
  accepted: "Die Person hat angenommen",
  completed: "Abgeschlossen",
  declined: "Von der Person abgelehnt",
  withdrawn: "Von euch zurückgezogen",
};
