import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import {
  Alert,
  Button,
  Card,
  Empty,
  Loading,
  Page,
  Row,
  RowList,
  Switch,
} from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import {
  attachmentUrl,
  getMyPortfolio,
  getPortfolioVisibility,
  setPortfolioVisibility,
} from "../portfolio/client";

export interface PortfolioRouteProps {
  principal?: MeResponse | null;
}

/** Rolle und Jahr, soweit angegeben — und ob eine Datei hängt. */
function einordnung(role: string, year: number | null, attachment: string | null): string {
  const teile: string[] = [];
  if (role !== "") teile.push(role);
  if (year !== null) teile.push(String(year));
  if (attachment !== null) teile.push("mit Datei");
  return teile.join(" · ");
}

/**
 * Meine Arbeiten — die Liste und die Freigabe.
 *
 * Die Formulare liegen auf eigenen Adressen (`/portfolio/new`,
 * `/portfolio/<nummer>`). Vorher standen alle Arbeiten gleichzeitig als
 * aufgeklappte Formulare hier, und ein Speichern galt für alle zusammen.
 */
export function PortfolioRoute({ principal = null }: PortfolioRouteProps) {
  const queryClient = useQueryClient();
  const subjectId = principal?.user_id ?? null;
  const [error, setError] = useState<string | null>(null);

  const portfolioQuery = useQuery({
    queryKey: ["portfolio", "me"],
    queryFn: getMyPortfolio,
    enabled: subjectId !== null,
  });
  const visibilityQuery = useQuery({
    queryKey: ["portfolio", "visibility", subjectId],
    queryFn: () => getPortfolioVisibility(subjectId as string),
    enabled: subjectId !== null,
  });

  const toggle = useMutation({
    mutationFn: (next: boolean) => setPortfolioVisibility(subjectId as string, next),
    onSuccess: (result, next) => {
      if (result.ok) {
        setError(null);
        // Der Ledger sagt, was gilt — nicht der Wunsch des Klicks.
        queryClient.setQueryData(["portfolio", "visibility", subjectId], result.granted);
      } else {
        setError(result.message);
        queryClient.setQueryData(["portfolio", "visibility", subjectId], !next);
      }
    },
  });

  if (subjectId === null) {
    return (
      <Page title="Meine Arbeiten" narrow>
        <Card>
          <p>
            Bitte <a href="/login">anmelden</a>, um dein Portfolio zu bearbeiten.
          </p>
        </Card>
      </Page>
    );
  }

  if (portfolioQuery.isPending) {
    return (
      <Page title="Meine Arbeiten" narrow>
        <Card>
          <Loading label="Portfolio wird geladen…" />
        </Card>
      </Page>
    );
  }

  const items = portfolioQuery.data?.items ?? [];
  const hasItems = items.length > 0;
  // Die ANZEIGE bleibt bei Nichtwissen aus — ein Schalter, der versehentlich
  // „freigegeben" behauptet, ist die gefährlichere Lüge.
  const released = visibilityQuery.data === true;
  // ... aber „weiß ich nicht" ist nicht „nein". Dieselbe Korrektur wie auf der
  // Profilseite: `null` heißt „der Ledger hat nicht geantwortet", und dann darf
  // der Schalter nicht bedienbar sein.
  const ledgerSilent = !visibilityQuery.isPending && visibilityQuery.data === null;
  const ledgerUnknown = visibilityQuery.isPending || ledgerSilent;

  return (
    <Page
      title="Meine Arbeiten"
      narrow
      lead="Ein Schaufenster: hier steht, was du zeigen willst. Was du nicht zeigen darfst, gehört nicht hierher — dafür gibt es keine halbe Sichtbarkeit."
    >
      {error !== null ? <Alert>{error}</Alert> : null}

      <Card className="profile__release">
        <Switch
          label="Arbeiten für Unternehmen freigeben"
          checked={released}
          disabled={!hasItems || toggle.isPending || ledgerUnknown}
          hint={
            ledgerSilent
              ? "Ob eine Freigabe gilt, ist gerade nicht abrufbar. Solange das so ist, ändert dieser Schalter nichts — sonst würdest du etwas freigeben, dessen Stand niemand kennt."
              : visibilityQuery.isPending
                ? "Freigabe wird geprüft…"
                : hasItems
                  ? "Eigene Freigabe, getrennt vom Profil: du kannst ansprechbar sein, ohne deine Arbeiten zu zeigen. Wirkt sofort."
                  : "Erst eine Arbeit speichern — freigeben lässt sich nur, was es gibt."
          }
          onChange={(next) => toggle.mutate(next)}
        />
      </Card>

      <Card>
        {!hasItems ? (
          <Empty
            title="Noch keine Arbeit eingetragen."
            hint="Was hier steht, entscheidest du — und wer es sieht, auch."
            action={<Button href="/portfolio/new">Arbeit hinzufügen</Button>}
          />
        ) : (
          <>
            {/* Die Reihenfolge ist die des gespeicherten Feldes, und die Adresse
                einer Arbeit ist ihre Stelle darin: `PortfolioItem` hat keine ID
                (siehe Befund E3b). */}
            <RowList>
              {items.map((item, index) => (
                <Row
                  key={index}
                  title={item.title}
                  meta={einordnung(item.role, item.year, item.attachment)}
                  actions={
                    <>
                      <Button href={`/portfolio/${index}`} variant="quiet">
                        Bearbeiten
                      </Button>
                      {item.attachment !== null ? (
                        <a
                          href={attachmentUrl(subjectId, item.attachment)}
                          target="_blank"
                          rel="noreferrer noopener"
                        >
                          Datei ansehen
                        </a>
                      ) : null}
                    </>
                  }
                />
              ))}
            </RowList>
            <Button href="/portfolio/new">Arbeit hinzufügen</Button>
          </>
        )}
      </Card>
    </Page>
  );
}
