import { useQuery } from "@tanstack/react-query";
import { Alert, Button, Card, DescriptionList, Loading, Page } from "@workertransfer/ui";

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
        // `null` heißt „nicht abrufbar" — vorher stand hier immer `true`, und
        // der Export behauptete, er enthalte Einstellungen, die niemand gesetzt
        // hat (der Client erfand in diesem Fall die Voreinstellung).
        benachrichtigungen: section(
          benachrichtigungen !== null,
          benachrichtigungen ?? undefined
        ),
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
      <Page title="Meine Daten" narrow>
        <Card>
          <p>
            Bitte <a href="/login">anmelden</a>, um deine Daten zu sehen.
          </p>
        </Card>
      </Page>
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
    <Page
      title="Meine Daten"
      narrow
      lead="Alles, was diese Plattform über dich gespeichert hat, in einer Datei. Sie entsteht in deinem Browser und wird nirgends abgelegt — es gibt also nichts, das liegen bleibt."
    >
      <Card>
        {query.isPending ? <Loading label="Daten werden gesammelt…" /> : null}

        {missing.length > 0 ? (
          // Vor dem Herunterladen, nicht erst in der Datei.
          <Alert>
            Diese Teile konnten nicht geladen werden: {missing.join(", ")}. Die Datei sagt das
            ebenfalls — sie ist unvollständig.
          </Alert>
        ) : null}

        {result !== null ? (
          <>
            {/* Ein `<dl>`, keine Liste: zu jedem Abschnitt gehört die Auskunft,
                ob er enthalten ist. Diese Zuordnung ist der ganze Inhalt der
                Aufstellung und steht damit im Markup statt im Layout. */}
            <DescriptionList
              items={Object.entries(result.abschnitte).map(([name, entry]) => ({
                term: name.replace(/_/g, " "),
                description: entry.status === "ok" ? "enthalten" : "fehlt",
              }))}
            />
            <Button onClick={download}>Als JSON herunterladen</Button>
          </>
        ) : null}
      </Card>

      <Card>
        <h2>Was hier nicht steht</h2>
        <p className="requests__meta">
          Löschen ist ein eigener Weg und steht bewusst nicht als Knopf neben einem
          Herunterladen-Knopf: hier lässt sich nichts falsch anklicken, was sich nicht rückgängig
          machen ließe. Was dabei passiert, steht vollständig auf{" "}
          <a href="/delete-account">Konto löschen</a> — vor dem Klick, nicht danach.
        </p>
        <p className="wt-field__hint">
          Du musst hier nichts herunterladen, bevor du löschst. Der Verweis geht in beide
          Richtungen, damit niemand glaubt, es gäbe eine Pflichtreihenfolge.
        </p>
      </Card>
    </Page>
  );
}
