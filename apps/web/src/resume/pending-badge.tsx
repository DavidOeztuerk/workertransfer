import { useQuery } from "@tanstack/react-query";

import { listMyRequests } from "./client";

/**
 * Wie viele Anfragen noch auf eine Antwort warten.
 *
 * Ohne diesen Hinweis erfährt eine Person von einer Anfrage nur, wenn sie
 * zufällig `/resume` aufruft — der ganze Anfragefluss liefe ins Leere. Eine
 * Mail wäre der nächste Schritt, braucht aber eigene Einstellungen und eine
 * eigene Einwilligung; bis dahin ist dies die ehrliche kleine Lösung.
 *
 * Bei einem Fehler zeigt der Zähler nichts. Eine Zahl, die auf einer
 * fehlgeschlagenen Abfrage beruht, wäre schlimmer als keine: sie würde
 * entweder unnötig beunruhigen oder in Sicherheit wiegen.
 */
export function PendingRequestBadge() {
  const query = useQuery({
    queryKey: ["resume", "requests", "me"],
    queryFn: listMyRequests,
    // Dieselbe Abfrage wie auf /resume — der Cache teilt sie, die Seite und
    // die Kopfzeile zeigen also nie verschiedene Stände.
    staleTime: 30_000,
  });

  const result = query.data;
  if (result === undefined || !result.ok) return null;

  const pending = result.requests.filter((request) => request.status === "PENDING").length;
  if (pending === 0) return null;

  const label = pending === 1 ? "1 offene Anfrage" : `${pending} offene Anfragen`;
  return (
    <span className="badge" aria-label={label}>
      {pending}
    </span>
  );
}
