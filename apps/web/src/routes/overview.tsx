import { useQuery } from "@tanstack/react-query";
import { Card } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import { listMyMarketRequests } from "../market/client";
import { listMyRequests } from "../resume/client";
import { listCompanyTransfers, listMyTransfers } from "../transfers/client";

export interface OverviewRouteProps {
  principal: MeResponse;
}

interface Item {
  label: string;
  href: string;
}

/**
 * Was auf mich wartet — zusammengesetzt im Browser, nicht im Backend.
 *
 * Ein Dienst, der diese Übersicht liefert, müsste über vier Dienstgrenzen
 * hinweg lesen — genau das, was ADR-0004 ausschließt. Die Oberfläche fragt
 * jeden Dienst nach dem, wofür er zuständig ist, und legt die Antworten
 * nebeneinander. Es sind dieselben Abfragen, die die Einzelseiten ohnehin
 * stellen; der Cache teilt sie.
 *
 * Gezählt wird nur, was eine HANDLUNG erwartet. Eine Übersicht, die auch
 * anzeigt, was gerade von selbst läuft, ist eine Liste — und eine Liste
 * übersieht man.
 */
export function OverviewRoute({ principal }: OverviewRouteProps) {
  const hasCompany = principal.tenant_id != null;

  const marketRequests = useQuery({
    queryKey: ["market", "requests", "me"],
    queryFn: listMyMarketRequests,
    staleTime: 30_000,
  });
  const resumeRequests = useQuery({
    queryKey: ["resume", "requests", "me"],
    queryFn: listMyRequests,
    staleTime: 30_000,
  });
  const myTransfers = useQuery({
    queryKey: ["transfers", "me"],
    queryFn: listMyTransfers,
    staleTime: 30_000,
  });
  const companyTransfers = useQuery({
    queryKey: ["transfers", "company", principal.tenant_id ?? null],
    queryFn: listCompanyTransfers,
    enabled: hasCompany,
    staleTime: 30_000,
  });

  // Bei einem Fehler wird nichts gezählt statt null gezählt. Eine Zahl, die auf
  // einer fehlgeschlagenen Abfrage beruht, wiegt in Sicherheit — und „nichts
  // liegt an" ist genau die Aussage, die dann falsch wäre.
  const failed =
    (marketRequests.data !== undefined && !marketRequests.data.ok) ||
    (resumeRequests.data !== undefined && !resumeRequests.data.ok) ||
    (myTransfers.data !== undefined && !myTransfers.data.ok) ||
    (companyTransfers.data !== undefined && !companyTransfers.data.ok);

  const mine: Item[] = [];

  const openMarket =
    marketRequests.data?.ok === true
      ? marketRequests.data.requests.filter((r) => r.status === "PENDING").length
      : 0;
  if (openMarket > 0) {
    mine.push({
      label:
        openMarket === 1
          ? "1 Unternehmen möchte sehen, ob du ansprechbar bist"
          : `${openMarket} Unternehmen möchten sehen, ob du ansprechbar bist`,
      href: "/markt",
    });
  }

  const openResume =
    resumeRequests.data?.ok === true
      ? resumeRequests.data.requests.filter((r) => r.status === "PENDING").length
      : 0;
  if (openResume > 0) {
    mine.push({
      label:
        openResume === 1
          ? "1 Anfrage nach deinem Lebenslauf"
          : `${openResume} Anfragen nach deinem Lebenslauf`,
      href: "/resume",
    });
  }

  const waitingOnMe =
    myTransfers.data?.ok === true
      ? myTransfers.data.transfers.filter(
          (t) =>
            t.status === "interested" ||
            t.status === "offered" ||
            (t.status === "accepted" && t.requires_release && !t.release_confirmed)
        ).length
      : 0;
  if (waitingOnMe > 0) {
    mine.push({
      label:
        waitingOnMe === 1
          ? "1 Gespräch wartet auf dich"
          : `${waitingOnMe} Gespräche warten auf dich`,
      href: "/transfers",
    });
  }

  const company: Item[] = [];
  const waitingOnCompany =
    companyTransfers.data?.ok === true
      ? companyTransfers.data.transfers.filter(
          (t) => t.status === "talking" || (t.status === "accepted" && !t.requires_release)
        ).length
      : 0;
  if (waitingOnCompany > 0) {
    company.push({
      label:
        waitingOnCompany === 1
          ? "1 Transfer wartet auf euch"
          : `${waitingOnCompany} Transfers warten auf euch`,
      href: "/company/transfers",
    });
  }

  const nothing = mine.length === 0 && company.length === 0;

  return (
    <main className="page page--narrow">
      <header className="page__header">
        <h1>Was liegt an</h1>
        <p className="page__lead">
          Nur Dinge, die auf eine Entscheidung von dir warten. Was von selbst läuft, steht hier
          nicht — sonst wäre es eine Liste, und Listen übersieht man.
        </p>
      </header>

      {failed ? (
        <Card>
          <p className="auth__alert" role="alert">
            Ein Teil konnte nicht geladen werden. Was hier steht, ist deshalb womöglich
            unvollständig.
          </p>
        </Card>
      ) : null}

      {nothing && !failed ? (
        <Card>
          <p>Gerade wartet nichts auf dich.</p>
          <p className="requests__meta">
            Du entscheidest, was von dir sichtbar ist — nachsehen kannst du das jederzeit unter{" "}
            <a href="/freigaben">Meine Freigaben</a>.
          </p>
        </Card>
      ) : null}

      {mine.length > 0 ? (
        <Card>
          <h2>Für dich</h2>
          <ul className="overview">
            {mine.map((item) => (
              <li key={item.href}>
                <a href={item.href}>{item.label}</a>
              </li>
            ))}
          </ul>
        </Card>
      ) : null}

      {company.length > 0 ? (
        <Card>
          <h2>Für dein Unternehmen</h2>
          <ul className="overview">
            {company.map((item) => (
              <li key={item.href}>
                <a href={item.href}>{item.label}</a>
              </li>
            ))}
          </ul>
        </Card>
      ) : null}
    </main>
  );
}
