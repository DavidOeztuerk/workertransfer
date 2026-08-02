import { useMutation, useQueries, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button, Card } from "@workertransfer/ui";

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
      <main className="page page--narrow">
        <Card>
          <h1>Meine Freigaben</h1>
          <p>
            Bitte <a href="/login">anmelden</a>, um deine Freigaben zu sehen.
          </p>
        </Card>
      </main>
    );
  }

  return (
    <main className="page page--narrow">
      <header className="page__header">
        <h1>Meine Freigaben</h1>
        <p className="page__lead">
          Alles, was gerade gilt — an einer Stelle. Zurückziehen wirkt sofort: der nächste Zugriff
          läuft ins Leere, ohne Umweg über uns. Was hier nicht steht, sieht niemand.
        </p>
      </header>

      {error !== null ? (
        <p className="auth__alert" role="alert">
          {error}
        </p>
      ) : null}

      {query.isPending ? (
        <Card>
          <p role="status">Freigaben werden geladen…</p>
        </Card>
      ) : null}

      {query.data !== undefined && !query.data.ok ? (
        <Card>
          {/* Kein leerer Zustand bei einem Fehler: „du hast nichts freigegeben"
              wäre hier die beruhigendste falsche Antwort, die es gibt. */}
          <p className="auth__alert" role="alert">
            {query.data.message}
          </p>
        </Card>
      ) : null}

      {query.data?.ok && consents.length === 0 ? (
        <Card>
          <p>Du hast im Moment nichts freigegeben. Niemand sieht etwas von dir.</p>
        </Card>
      ) : null}

      {consents.length > 0 ? (
        <Card>
          <ul className="requests">
            {consents.map((consent) => (
              <li key={consent.capability}>
                <ConsentRow
                  consent={consent}
                  companyName={
                    nameById.get(parseCapability(consent.capability).tenantId ?? "") ?? null
                  }
                  busy={withdraw.isPending}
                  onWithdraw={() => withdraw.mutate(consent.capability)}
                />
              </li>
            ))}
          </ul>
        </Card>
      ) : null}
    </main>
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
    <div className="requests__row">
      <div>
        <p className="requests__title">
          {what} · {who}
        </p>
        <p className="requests__meta">
          Freigegeben am {new Date(consent.granted_at).toLocaleDateString("de-DE")}
        </p>
      </div>
      <div className="requests__actions">
        <Button variant="quiet" onClick={onWithdraw} disabled={busy}>
          Zurückziehen
        </Button>
      </div>
    </div>
  );
}
