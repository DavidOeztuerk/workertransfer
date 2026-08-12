import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Alert, Button, Card, Empty, Loading, Page, Row, RowList } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import { getCompanyProfile } from "../companies/client";
import {
  type GrantedConsent,
  listMyConsents,
  parseCapability,
  setGranted,
} from "../consent/client";

export interface ConsentsRouteProps {
  principal?: MeResponse | null;
}

const WITHDRAWAL_REASON = "Auf der Seite Meine Freigaben zurückgezogen";

/**
 * Die Seite, die einer Consent-Plattform gefehlt hat.
 *
 * Vorher waren die Freigaben über vier Seiten verstreut, und die aus einer
 * Bewerbung standen auf keiner davon. Eine Einwilligung, die man nicht
 * überblicken kann, ist keine informierte Einwilligung, und ein Widerruf, den
 * man nicht findet, ist keiner.
 */
export function ConsentsRoute({ principal = null }: ConsentsRouteProps) {
  const queryClient = useQueryClient();
  const subjectId = principal?.user_id ?? null;
  const [error, setError] = useState<string | null>(null);

  const query = useQuery({
    queryKey: ["my-consents"],
    queryFn: listMyConsents,
    enabled: subjectId !== null,
  });

  const consents: GrantedConsent[] = query.data?.ok === true ? query.data.consents : [];
  const tenantIds = [
    ...new Set(
      consents
        .map((consent) => parseCapability(consent.capability).tenantId)
        .filter((id): id is string => id !== null)
    ),
  ];

  // Die Namen kommen frisch vom companies-service. Sie im Ledger zu führen
  // hieße, eine Kopie zu halten, die veraltet, sobald ein Unternehmen sich
  // umbenennt — und der Ledger verwaltet Fähigkeiten, keine Unternehmen.
  const names = useQueries({
    queries: tenantIds.map((tenantId) => ({
      queryKey: ["company-profile", tenantId],
      queryFn: () => getCompanyProfile(tenantId),
    })),
  });
  const nameById = new Map<string, string>();
  tenantIds.forEach((tenantId, index) => {
    const profile = names[index]?.data;
    if (profile != null) nameById.set(tenantId, profile.display_name);
  });

  const withdraw = useMutation({
    mutationFn: (capability: string) =>
      setGranted(subjectId ?? "", capability, false, WITHDRAWAL_REASON),
    onSuccess: (result) => {
      setError(result.ok ? null : result.message);
      void queryClient.invalidateQueries({ queryKey: ["my-consents"] });
    },
  });

  if (subjectId === null) {
    return (
      <Page title="Meine Freigaben" narrow>
        <Card>
          <p>
            Bitte <a href="/login">anmelden</a>, um deine Freigaben zu sehen.
          </p>
        </Card>
      </Page>
    );
  }

  return (
    <Page
      title="Meine Freigaben"
      narrow
      lead="Alles, was gerade gilt — an einer Stelle. Zurückziehen wirkt sofort: der nächste Zugriff läuft ins Leere, ohne Umweg über uns. Was hier nicht steht, sieht niemand."
    >
      {error !== null ? <Alert>{error}</Alert> : null}

      <Card>
        {/* Reihenfolge nach dem Muster: lädt, dann Fehler, dann leer, dann
            Inhalt. */}
        {query.isPending ? <Loading label="Freigaben werden geladen…" /> : null}

        {/* Kein leerer Zustand bei einem Fehler: „du hast nichts freigegeben"
            wäre hier die beruhigendste falsche Antwort, die es gibt. */}
        {query.data !== undefined && !query.data.ok ? <Alert>{query.data.message}</Alert> : null}

        {query.data?.ok && consents.length === 0 ? (
          <Empty
            title="Du hast im Moment nichts freigegeben."
            hint="Niemand sieht etwas von dir."
          />
        ) : null}

        {consents.length > 0 ? (
          <RowList>
            {consents.map((consent) => (
              <ConsentRow
                key={consent.capability}
                consent={consent}
                companyName={
                  nameById.get(parseCapability(consent.capability).tenantId ?? "") ?? null
                }
                busy={withdraw.isPending}
                onWithdraw={() => withdraw.mutate(consent.capability)}
              />
            ))}
          </RowList>
        ) : null}
      </Card>
    </Page>
  );
}

function ConsentRow({
  consent,
  companyName,
  busy,
  onWithdraw,
}: {
  consent: GrantedConsent;
  companyName: string | null;
  busy: boolean;
  onWithdraw: () => void;
}) {
  const parsed = parseCapability(consent.capability);
  // Eine unbekannte Form wird gezeigt, nicht verschluckt — und lässt sich
  // trotzdem zurückziehen.
  const what = parsed.area ?? consent.capability;
  const who = parsed.public
    ? "Alle Unternehmen"
    : parsed.tenantId !== null
      ? (companyName ?? "Ein Unternehmen")
      : "Empfänger unbekannt";

  return (
    <Row
      title={`${what} · ${who}`}
      meta={`Freigegeben am ${new Date(consent.granted_at).toLocaleDateString("de-DE")}`}
      actions={
        <Button variant="quiet" onClick={onWithdraw} disabled={busy}>
          Zurückziehen
        </Button>
      }
    />
  );
}
