import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Alert, Button, Card, Field, Page, Select, TextArea } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import { type EmploymentType, type RemoteMode, createJob, draftJobText } from "../jobs/client";
import { parseSkills } from "../skills";

export interface CompanyJobNewRouteProps {
  principal?: MeResponse | null;
}

interface Draft {
  title: string;
  description: string;
  location: string;
  remote: RemoteMode;
  employment: EmploymentType;
  /** Als Zeile im Formular; zerlegt wird erst beim Abschicken. */
  skills: string;
}

const EMPTY: Draft = {
  title: "",
  description: "",
  location: "",
  remote: "none",
  employment: "full_time",
  skills: "",
};

/**
 * Eine Stelle anlegen — auf einer eigenen Adresse.
 *
 * Vorher stand dieses Formular über der Liste der bestehenden Stellen, und beide
 * teilten sich eine Seite: oben schreiben, unten verwalten. Getrennt ist beides
 * für sich verständlich, und die Liste zeigt danach das Ergebnis.
 *
 * Angelegt wird **immer ein Entwurf**. Veröffentlichen ist ein zweiter,
 * bewusster Schritt auf der Liste — ein Knopf, der beides täte, hätte die Stelle
 * draußen, bevor jemand sie gelesen hat.
 */
export function CompanyJobNewRoute({ principal = null }: CompanyJobNewRouteProps) {
  const queryClient = useQueryClient();
  const tenantId = principal?.tenant_id ?? null;
  const [draft, setDraft] = useState<Draft>(EMPTY);
  const [error, setError] = useState<string | null>(null);

  const create = useMutation({
    mutationFn: () => createJob({ ...draft, skills: parseSkills(draft.skills) }),
    onSuccess: (result) => {
      if (result.ok) {
        setError(null);
        void queryClient.invalidateQueries({ queryKey: ["jobs", "own", tenantId] });
        window.location.href = "/company/jobs";
      } else {
        setError(result.message);
      }
    },
  });

  const zurueck = <a href="/company/jobs">Zurück zu unseren Stellen</a>;

  if (tenantId === null) {
    return (
      <Page title="Neue Stelle" narrow back={zurueck}>
        <Card>
          <p>
            Wähle oben ein Unternehmen — oder lass dich von jemandem aus deinem Unternehmen
            einladen.
          </p>
        </Card>
      </Page>
    );
  }

  return (
    <Page
      title="Neue Stelle"
      narrow
      back={zurueck}
      lead="Ein Entwurf sieht niemand außer euch. Veröffentlicht wird er erst auf der Liste — in einem zweiten, bewussten Schritt."
    >
      <Card>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            create.mutate();
          }}
        >
          <Field
            label="Titel"
            value={draft.title}
            onChange={(e) => setDraft({ ...draft, title: e.target.value })}
            maxLength={160}
            required
          />
          <TextArea
            label="Beschreibung"
            rows={6}
            value={draft.description}
            onChange={(e) => setDraft({ ...draft, description: e.target.value })}
            maxLength={20000}
            required
          />
          <Field
            label="Ort"
            hint="Leer lassen, wenn es keinen festen gibt."
            value={draft.location}
            onChange={(e) => setDraft({ ...draft, location: e.target.value })}
            maxLength={160}
          />
          {/* Freiwillig. Eine erzwungene Liste wäre eine, die jemand ausfüllt,
              um das Formular loszuwerden — und Suchende glichen sich dann
              gegen Erfundenes ab. */}
          <Field
            label="Gesuchte Fähigkeiten"
            hint="Mit Komma getrennt, höchstens 20. Wer sich das ansieht, sieht, was ihm davon fehlt — eine Note vergibt niemand. Bekannte Schreibweisen vereinheitlichen wir, damit „Postgres“ und „PostgreSQL“ sich finden."
            value={draft.skills}
            onChange={(e) => setDraft({ ...draft, skills: e.target.value })}
          />
          <Select
            label="Arbeitsform"
            value={draft.remote}
            onChange={(e) => setDraft({ ...draft, remote: e.target.value as RemoteMode })}
          >
            <option value="none">Vor Ort</option>
            <option value="hybrid">Hybrid</option>
            <option value="full">Vollständig remote</option>
          </Select>
          <JobDraftHelp
            draft={draft}
            onDraft={(text) => setDraft((current) => ({ ...current, description: text }))}
          />
          {error !== null ? <Alert>{error}</Alert> : null}
          <Button type="submit" disabled={create.isPending}>
            {create.isPending ? "Wird angelegt…" : "Entwurf anlegen"}
          </Button>
        </form>
      </Card>
    </Page>
  );
}

/**
 * Der Unternehmens-Agent — und der Unterschied zum Profil-Agenten ist der Punkt.
 *
 * Beide schreiben einen Entwurf auf Knopfdruck und speichern nichts. Aber der
 * hier arbeitet an einem Text, den das Unternehmen **selbst verfasst hat**, und
 * sagt über keine Person etwas. Das ist der Grund, warum genau dieser der
 * einzige Unternehmens-Agent aus dem ULTRAPLAN ist, der ohne eigene Abwägung
 * gebaut werden konnte: Scout, Candidate Ranking, Salary Recommendation und
 * Team Analyzer richten sich alle auf Menschen (ADR-0022/0024).
 *
 * Der Hinweis nennt deshalb auch nicht „deine Daten“, sondern was wirklich
 * hinausgeht: der Anzeigentext. Und er nennt, was der Entwurf nicht tun wird —
 * Anforderungen dazuerfinden. Wer die Regel kennt, prüft den Vorschlag darauf.
 */
function JobDraftHelp({ draft, onDraft }: { draft: Draft; onDraft: (text: string) => void }) {
  const [wish, setWish] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const hasText = draft.description.trim() !== "";

  const ask = useMutation({
    mutationFn: () =>
      draftJobText({
        title: draft.title,
        description: draft.description,
        location: draft.location,
        skills: parseSkills(draft.skills),
        wish,
      }),
    onSuccess: (result) => {
      if (result.ok) {
        setProblem(null);
        onDraft(result.draft);
      } else {
        setProblem(result.message);
      }
    },
  });

  return (
    <div className="draft-help">
      <Field
        label={hasText ? "Anzeige umformulieren lassen" : "Beim Schreiben helfen lassen"}
        hint="Optional: was euch wichtig ist („kürzer“, „weniger Floskeln“). Titel, Beschreibung, Ort und die gesuchten Fähigkeiten gehen dafür an Anthropic — nichts über Bewerbende. Anforderungen erfindet der Vorschlag keine dazu, und gespeichert wird er erst, wenn ihr den Entwurf anlegt."
        value={wish}
        onChange={(e) => setWish(e.target.value)}
        maxLength={200}
      />
      {problem !== null ? <Alert>{problem}</Alert> : null}
      {/* type="button" ausgeschrieben, obwohl `Button` es ohnehin so vorgibt:
          dieser Knopf steht im selben <form> wie „Entwurf anlegen", und wer
          hier liest, soll nicht erst das UI-Paket aufschlagen müssen, um zu
          wissen, welcher der beiden absendet. Ein Test hält das Attribut fest —
          änderte sich die Vorgabe, legte dieser Knopf sonst die Stelle an. */}
      <Button type="button" variant="quiet" onClick={() => ask.mutate()} disabled={ask.isPending}>
        {ask.isPending
          ? "Wird geschrieben…"
          : hasText
            ? "Vorschlag holen (ersetzt die Beschreibung)"
            : "Vorschlag holen"}
      </Button>
    </div>
  );
}
