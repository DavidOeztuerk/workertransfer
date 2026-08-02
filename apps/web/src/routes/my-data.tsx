import { useQuery } from "@tanstack/react-query";
import { Button, Card } from "@workertransfer/ui";

import { type MeResponse, fetchMe } from "../auth/client";
import { listMyApplications } from "../applications/client";
import { listMyConsentHistory, listMyConsents } from "../consent/client";
import { type Section, buildExport, exportFilename, section } from "../export/build";
import { listMyMarketRequests, getMyMarketStatus } from "../market/client";
import { getMyProfile } from "../profile/client";
import { getMyPortfolio } from "../portfolio/client";
import { getMyResume, listMyRequests } from "../resume/client";
import { getNotificationPreferences } from "../settings/client";
import { listMyTransfers } from "../transfers/client";

export interface MyDataRouteProps {
  principal?: MeResponse | null;
}

/**
 * Auskunft und Mitnahme.
 *
 * Zusammengesetzt im Browser: ein Dienst, der alles einsammelt, müsste über
 * sieben Dienstgrenzen hinweg lesen (ADR-0004). Und die Datei entsteht hier und
 * wird nirgends gespeichert — es gibt nichts, das liegen bleibt, ablaufen muss
 * oder versehentlich geteilt wird.
 */
export function MyDataRoute({ principal = null }: MyDataRouteProps) {
  const subjectId = principal?.user_id ?? null;

  const query = useQuery({
    queryKey: ["my-data", subjectId],
    enabled: subjectId !== null,
    queryFn: async (): Promise<Record<string, Section>> => {
      const [
        konto,
        benachrichtigungen,
        profil,
        lebenslauf,
        lebenslaufAnfragen,
        portfolio,
        marktstatus,
        marktAnfragen,
        transfers,
        bewerbungen,
        freigaben,
        freigabenVerlauf,
      ] = await Promise.all([
        fetchMe().catch(() => null),
        getNotificationPreferences(),
        getMyProfile(),
        getMyResume(),
        listMyRequests(),
        getMyPortfolio(),
        getMyMarketStatus(),
        listMyMarketRequests(),
        listMyTransfers(),
        listMyApplications(),
        listMyConsents(),
        listMyConsentHistory(),
      ]);

      return {
        // `null` ist eine gültige Auskunft („noch keins") und darum `ok`.
        // Nur wo die Antwort ausblieb, steht „nicht abrufbar".
        konto: section(konto !== null, konto),
        benachrichtigungen: section(true, benachrichtigungen),
        profil: section(true, profil),
        lebenslauf: section(true, lebenslauf),
        lebenslauf_anfragen: section(
          lebenslaufAnfragen.ok,
          lebenslaufAnfragen.ok ? lebenslaufAnfragen.requests : undefined
        ),
        portfolio: section(true, portfolio),
        marktstatus: section(true, marktstatus),
        markt_anfragen: section(
          marktAnfragen.ok,
          marktAnfragen.ok ? marktAnfragen.requests : undefined
        ),
        transfers: section(transfers.ok, transfers.ok ? transfers.transfers : undefined),
        bewerbungen: section(
          bewerbungen.ok,
          bewerbungen.ok ? bewerbungen.applications : undefined
        ),
        freigaben: section(freigaben.ok, freigaben.ok ? freigaben.consents : undefined),
        freigaben_verlauf: section(
          freigabenVerlauf.ok,
          freigabenVerlauf.ok ? freigabenVerlauf.events : undefined
        ),
      };
    },
  });

  if (subjectId === null) {
    return (
      <main className="page page--narrow">
        <Card>
          <h1>Meine Daten</h1>
          <p>
            Bitte <a href="/login">anmelden</a>, um deine Daten zu sehen.
          </p>
        </Card>
      </main>
    );
  }

  const sections = query.data;
  const result = sections === undefined ? null : buildExport(sections);
  const missing = result?.unvollständig ?? [];

  function download() {
    if (result === null) return;
    const blob = new Blob([JSON.stringify(result, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = exportFilename();
    link.click();
    URL.revokeObjectURL(url);
  }

  return (
    <main className="page page--narrow">
      <header className="page__header">
        <h1>Meine Daten</h1>
        <p className="page__lead">
          Alles, was diese Plattform über dich gespeichert hat, in einer Datei. Sie entsteht in
          deinem Browser und wird nirgends abgelegt — es gibt also nichts, das liegen bleibt.
        </p>
      </header>

      <Card>
        {query.isPending ? <p role="status">Daten werden gesammelt…</p> : null}

        {missing.length > 0 ? (
          // Vor dem Herunterladen, nicht erst in der Datei.
          <p className="auth__alert" role="alert">
            Diese Teile konnten nicht geladen werden: {missing.join(", ")}. Die Datei sagt das
            ebenfalls — sie ist unvollständig.
          </p>
        ) : null}

        {result !== null ? (
          <>
            <ul className="overview">
              {Object.entries(result.abschnitte).map(([name, entry]) => (
                <li key={name}>
                  {name.replace(/_/g, " ")} — {entry.status === "ok" ? "enthalten" : "fehlt"}
                </li>
              ))}
            </ul>
            <Button onClick={download}>Als JSON herunterladen</Button>
          </>
        ) : null}
      </Card>

      <Card>
        <h2>Was hier nicht steht</h2>
        <p className="requests__meta">
          Löschen ist ein eigener Weg mit eigenen Fragen — Aufbewahrungspflichten, und Vorgänge
          wie Bewerbungen und Transfers, die nicht allein dir gehören. Er steht bewusst nicht
          neben einem Herunterladen-Knopf.
        </p>
      </Card>
    </main>
  );
}
