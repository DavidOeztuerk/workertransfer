import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Button, Card, Field, Switch, TextArea } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import {
  type Portfolio,
  type PortfolioItem,
  getMyPortfolio,
  getPortfolioVisibility,
  saveMyPortfolio,
  setPortfolioVisibility,
} from "../portfolio/client";

export interface PortfolioRouteProps {
  principal?: MeResponse | null;
}

/** Was im Formular steht — alles als Text, damit „leer" nicht zu 0 wird. */
interface Row {
  title: string;
  summary: string;
  url: string;
  role: string;
  year: string;
}

const EMPTY: Row = { title: "", summary: "", url: "", role: "", year: "" };

function toRows(portfolio: Portfolio | null | undefined): Row[] {
  if (portfolio === null || portfolio === undefined) return [];
  return portfolio.items.map((item) => ({
    title: item.title,
    summary: item.summary,
    url: item.url ?? "",
    role: item.role,
    year: item.year === null ? "" : String(item.year),
  }));
}

/**
 * Leere Felder werden `null`, nicht `""` oder `0`.
 *
 * Ein leerer String im URL-Feld würde später als Link gerendert und ins Nichts
 * führen; eine 0 im Jahr wäre eine Jahresangabe, die niemand gemeint hat.
 */
function toItem(row: Row): PortfolioItem {
  const year = row.year.trim();
  return {
    title: row.title,
    summary: row.summary,
    url: row.url.trim() === "" ? null : row.url.trim(),
    role: row.role,
    year: year === "" ? null : Number(year),
  };
}

export function PortfolioRoute({ principal = null }: PortfolioRouteProps) {
  const queryClient = useQueryClient();
  const subjectId = principal?.user_id ?? null;

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

  const [rows, setRows] = useState<Row[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  const loaded = portfolioQuery.data;
  useEffect(() => {
    if (loaded !== undefined) setRows(toRows(loaded));
  }, [loaded]);

  const save = useMutation({
    mutationFn: () => saveMyPortfolio(rows.map(toItem)),
    onSuccess: (result) => {
      if (result.ok) {
        setError(null);
        setSaved(true);
        queryClient.setQueryData(["portfolio", "me"], result.portfolio);
      } else {
        setSaved(false);
        setError(result.message);
      }
    },
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
      <main className="page page--narrow">
        <Card>
          <h1>Meine Arbeiten</h1>
          <p>
            Bitte <a href="/login">anmelden</a>, um dein Portfolio zu bearbeiten.
          </p>
        </Card>
      </main>
    );
  }

  if (portfolioQuery.isPending) {
    return (
      <main className="page page--narrow">
        <Card>
          <p role="status">Portfolio wird geladen…</p>
        </Card>
      </main>
    );
  }

  const hasItems = rows.length > 0 && portfolioQuery.data != null;
  const released = visibilityQuery.data === true;

  function update(index: number, patch: Partial<Row>) {
    setSaved(false);
    setRows((current) => current.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  }

  return (
    <main className="page page--narrow">
      <header className="page__header">
        <h1>Meine Arbeiten</h1>
        <p className="page__lead">
          Ein Schaufenster: hier steht, was du zeigen willst. Was du nicht zeigen darfst, gehört
          nicht hierher — dafür gibt es keine halbe Sichtbarkeit.
        </p>
      </header>

      <Card className="profile__release">
        <Switch
          label="Arbeiten für Unternehmen freigeben"
          checked={released}
          disabled={!hasItems || toggle.isPending}
          hint={
            hasItems
              ? "Eigene Freigabe, getrennt vom Profil: du kannst ansprechbar sein, ohne deine Arbeiten zu zeigen. Wirkt sofort."
              : "Erst eine Arbeit speichern — freigeben lässt sich nur, was es gibt."
          }
          onChange={(next) => toggle.mutate(next)}
        />
      </Card>

      <Card>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate();
          }}
        >
          {rows.map((row, index) => (
            // Der Index als Schlüssel ist hier richtig: die Zeilen haben keine
            // eigene Identität, und ihre Reihenfolge IST die Aussage.
            <fieldset key={index} className="resume__position">
              <legend>Arbeit {index + 1}</legend>
              <Field
                label="Titel"
                value={row.title}
                onChange={(e) => update(index, { title: e.target.value })}
                maxLength={160}
                required
              />
              <TextArea
                label="Worum es geht"
                hint="Was es ist und was du daran gemacht hast."
                rows={4}
                value={row.summary}
                onChange={(e) => update(index, { summary: e.target.value })}
                maxLength={1000}
              />
              <Field
                label="Link"
                type="url"
                hint="Optional, und nur http oder https."
                placeholder="https://…"
                value={row.url}
                onChange={(e) => update(index, { url: e.target.value })}
              />
              <Field
                label="Deine Rolle"
                value={row.role}
                onChange={(e) => update(index, { role: e.target.value })}
                maxLength={160}
              />
              <Field
                label="Jahr"
                inputMode="numeric"
                placeholder="2024"
                value={row.year}
                onChange={(e) => update(index, { year: e.target.value })}
              />
              <Button
                type="button"
                variant="quiet"
                onClick={() => setRows((current) => current.filter((_, i) => i !== index))}
              >
                Arbeit entfernen
              </Button>
            </fieldset>
          ))}

          <Button
            type="button"
            variant="quiet"
            onClick={() => setRows((current) => [...current, { ...EMPTY }])}
          >
            Arbeit hinzufügen
          </Button>

          {error !== null ? (
            <p className="auth__alert" role="alert">
              {error}
            </p>
          ) : null}
          {saved && error === null ? <p className="page__note">Arbeiten gespeichert.</p> : null}

          <Button type="submit" disabled={save.isPending}>
            {save.isPending ? "Wird gespeichert…" : "Speichern"}
          </Button>
        </form>
      </Card>
    </main>
  );
}
