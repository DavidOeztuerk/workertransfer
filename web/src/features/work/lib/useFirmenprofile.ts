import { useEffect, useRef, useState } from "react";

import { type CompanyProfile, getCompanyProfile } from "../../company/api/companies";

/**
 * Die Firmenprofile zu einer Liste von Stellen — je Unternehmen einmal gefragt,
 * nicht je Stelle.
 *
 * Mehrere Stellen desselben Arbeitgebers stehen regelmäßig nebeneinander. Eine
 * Abfrage je Karte hieße, dieselbe Antwort fünfmal zu holen; das war im alten
 * Code über den Abfrageschlüssel von TanStack Query geregelt, und der fehlt hier
 * (kein `QueryClientProvider`, siehe `lib/useAsync.ts`).
 *
 * Ein fehlender Eintrag heißt „noch nicht gefragt", ein Eintrag `null` heißt
 * „hat kein Profil". Der Unterschied trägt: die Karte zeigt im ersten Fall
 * nichts, weil sie es nicht weiß, und im zweiten nichts, weil das Unternehmen
 * anonym bleibt — und niemals einen Platzhalter, den niemand geschrieben hat.
 */
export function useFirmenprofile(tenantIds: string[]): Record<string, CompanyProfile | null> {
  const [profile, setProfile] = useState<Record<string, CompanyProfile | null>>({});
  // Zwei Spiegel neben dem Zustand: `geladen` hält, was schon beantwortet ist,
  // `laeuft`, was gerade unterwegs ist. Ohne beide würde bei jedem Rendern neu
  // gefragt oder eine abgebrochene Anfrage nie nachgeholt.
  const loaded = useRef<Record<string, CompanyProfile | null>>({});
  const running = useRef<Set<string>>(new Set());

  // Eine Zeichenkette als Abhängigkeit: ein frisches Array wäre bei jedem
  // Rendern ein anderes und triebe den Effekt endlos.
  const key = [...new Set(tenantIds)].sort().join(",");

  useEffect(() => {
    const ids = key === "" ? [] : key.split(",");
    const open = ids.filter((id) => !(id in loaded.current) && !running.current.has(id));
    if (open.length === 0) return;
    for (const id of open) running.current.add(id);

    const abort = new AbortController();
    let alive = true;
    void Promise.all(
      open.map(async (id) => [id, await getCompanyProfile(id, abort.signal)] as const)
    ).then((paare) => {
      if (!alive) return;
      for (const [id, value] of paare) {
        loaded.current[id] = value;
        running.current.delete(id);
      }
      setProfile({ ...loaded.current });
    });

    return () => {
      alive = false;
      abort.abort();
      // Abgebrochene bleiben NICHT als „unterwegs" stehen — sonst fehlte der
      // Name für den Rest der Sitzung, weil niemand sie je wieder holt.
      for (const id of open) if (!(id in loaded.current)) running.current.delete(id);
    };
  }, [key]);

  return profile;
}
