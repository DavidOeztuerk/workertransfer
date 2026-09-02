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
  const geladen = useRef<Record<string, CompanyProfile | null>>({});
  const laeuft = useRef<Set<string>>(new Set());

  // Eine Zeichenkette als Abhängigkeit: ein frisches Array wäre bei jedem
  // Rendern ein anderes und triebe den Effekt endlos.
  const schluessel = [...new Set(tenantIds)].sort().join(",");

  useEffect(() => {
    const ids = schluessel === "" ? [] : schluessel.split(",");
    const offen = ids.filter((id) => !(id in geladen.current) && !laeuft.current.has(id));
    if (offen.length === 0) return;
    for (const id of offen) laeuft.current.add(id);

    const abbruch = new AbortController();
    let lebt = true;
    void Promise.all(
      offen.map(async (id) => [id, await getCompanyProfile(id, abbruch.signal)] as const)
    ).then((paare) => {
      if (!lebt) return;
      for (const [id, wert] of paare) {
        geladen.current[id] = wert;
        laeuft.current.delete(id);
      }
      setProfile({ ...geladen.current });
    });

    return () => {
      lebt = false;
      abbruch.abort();
      // Abgebrochene bleiben NICHT als „unterwegs" stehen — sonst fehlte der
      // Name für den Rest der Sitzung, weil niemand sie je wieder holt.
      for (const id of offen) if (!(id in geladen.current)) laeuft.current.delete(id);
    };
  }, [schluessel]);

  return profile;
}
