import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Alert, Button, Card, Empty, Loading, Page, Row, RowList } from "@workertransfer/ui";

import {
  type Application,
  type ApplicationStatus,
  listMyApplications,
  withdrawApplication,
} from "../applications/client";
import type { MeResponse } from "../auth/client";

export interface ApplicationsRouteProps {
  principal?: MeResponse | null;
}

/** Was der Zustand für die bewerbende Person bedeutet. */
const STATUS_LABEL: Record<ApplicationStatus, string> = {
  submitted: "Abgeschickt",
  reviewing: "Wird gelesen",
  rejected: "Abgelehnt",
  withdrawn: "Zurückgezogen — deine Daten sind wieder zu",
  hired: "Zusage",
};

/** Läuft die Bewerbung noch — und damit die Freigabe der Daten? */
function isLive(status: ApplicationStatus): boolean {
  return status === "submitted" || status === "reviewing";
}

/** Das Profil geht immer mit — ohne es wäre es keine Bewerbung. */
function freigegeben(application: Application): string {
  const teile = ["Profil"];
  if (application.shares_resume) teile.push("Lebenslauf");
  if (application.shares_portfolio) teile.push("Arbeiten");
  return teile.join(", ");
}

export function ApplicationsRoute({ principal = null }: ApplicationsRouteProps) {
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);

  const query = useQuery({
    queryKey: ["applications", "me"],
    queryFn: listMyApplications,
    enabled: principal !== null,
  });

  const withdraw = useMutation({
    mutationFn: (id: string) => withdrawApplication(id),
    onSuccess: (result) => {
      setError(result.ok ? null : result.message);
      void queryClient.invalidateQueries({ queryKey: ["applications", "me"] });
    },
  });

  if (principal === null) {
    return (
      <Page title="Meine Bewerbungen" narrow>
        <Card>
          <p>
            Bitte <a href="/login">anmelden</a>, um deine Bewerbungen zu sehen.
          </p>
        </Card>
      </Page>
    );
  }

  const result = query.data;

  return (
    <Page
      title="Meine Bewerbungen"
      narrow
      lead="Solange eine Bewerbung läuft, sieht das Unternehmen dein Profil — und was du sonst freigegeben hast. Ziehst du sie zurück, ist der Zugriff sofort zu; der Vorgang bleibt beim Unternehmen als das stehen, was er war."
    >
      {error !== null ? <Alert>{error}</Alert> : null}

      <Card>
        {/* Reihenfolge nach dem Muster: lädt, dann Fehler, dann leer, dann
            Inhalt. Der Ladezustand FEHLTE hier — solange die Liste unterwegs
            war, griff keiner der drei Zweige und man sah eine leere Karte. */}
        {query.isPending ? <Loading label="Bewerbungen werden geladen…" /> : null}
        {result !== undefined && !result.ok ? <Alert>{result.message}</Alert> : null}
        {result?.ok && result.applications.length === 0 ? (
          <Empty
            title="Noch keine Bewerbung."
            action={<a href="/jobs">Offene Stellen ansehen</a>}
          />
        ) : null}
        {result?.ok && result.applications.length > 0 ? (
          <RowList>
            {result.applications.map((application) => (
              <Row
                key={application.id}
                title={STATUS_LABEL[application.status]}
                meta={
                  isLive(application.status)
                    ? `Freigegeben: ${freigegeben(application)}`
                    : "Das Unternehmen sieht deine Daten nicht mehr."
                }
                // Zurückziehen nur, solange etwas freigegeben IST. Ein Knopf für
                // eine Bewerbung, die schon zu ist, wäre eine Lüge über den
                // Zustand — deshalb `undefined` und nicht ein deaktivierter
                // Knopf.
                actions={
                  isLive(application.status) ? (
                    <Button
                      variant="quiet"
                      onClick={() => withdraw.mutate(application.id)}
                      disabled={withdraw.isPending}
                    >
                      Zurückziehen
                    </Button>
                  ) : undefined
                }
              />
            ))}
          </RowList>
        ) : null}
      </Card>
    </Page>
  );
}
