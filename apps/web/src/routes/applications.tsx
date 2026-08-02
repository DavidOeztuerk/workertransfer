import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button, Card } from "@workertransfer/ui";

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
      <main className="page page--narrow">
        <Card>
          <h1>Meine Bewerbungen</h1>
          <p>
            Bitte <a href="/login">anmelden</a>, um deine Bewerbungen zu sehen.
          </p>
        </Card>
      </main>
    );
  }

  const result = query.data;

  return (
    <main className="page page--narrow">
      <header className="page__header">
        <h1>Meine Bewerbungen</h1>
        <p className="page__lead">
          Solange eine Bewerbung läuft, sieht das Unternehmen dein Profil — und was du sonst
          freigegeben hast. Ziehst du sie zurück, ist der Zugriff sofort zu; der Vorgang bleibt
          beim Unternehmen als das stehen, was er war.
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
        {result !== undefined && !result.ok ? (
          <p className="auth__alert" role="alert">
            {result.message}
          </p>
        ) : null}
        {result?.ok && result.applications.length === 0 ? (
          <p>
            Noch keine Bewerbung. <a href="/jobs">Offene Stellen ansehen</a>.
          </p>
        ) : null}
        {result?.ok && result.applications.length > 0 ? (
          <ul className="requests">
            {result.applications.map((application) => (
              <li key={application.id}>
                <Row
                  application={application}
                  busy={withdraw.isPending}
                  onWithdraw={() => withdraw.mutate(application.id)}
                />
              </li>
            ))}
          </ul>
        ) : null}
      </Card>
    </main>
  );
}

function Row({
  application,
  busy,
  onWithdraw,
}: {
  application: Application;
  busy: boolean;
  onWithdraw: () => void;
}) {
  const shared = ["Profil"];
  if (application.shares_resume) shared.push("Lebenslauf");
  if (application.shares_portfolio) shared.push("Arbeiten");

  return (
    <div className="requests__row">
      <div>
        <p className="requests__title">{STATUS_LABEL[application.status]}</p>
        <p className="requests__meta">
          {isLive(application.status)
            ? `Freigegeben: ${shared.join(", ")}`
            : "Das Unternehmen sieht deine Daten nicht mehr."}
        </p>
      </div>
      <div className="requests__actions">
        {/* Zurückziehen nur, solange etwas freigegeben IST. Ein Knopf für eine
            Bewerbung, die schon zu ist, wäre eine Lüge über den Zustand. */}
        {isLive(application.status) ? (
          <Button variant="quiet" onClick={onWithdraw} disabled={busy}>
            Zurückziehen
          </Button>
        ) : null}
      </div>
    </div>
  );
}
