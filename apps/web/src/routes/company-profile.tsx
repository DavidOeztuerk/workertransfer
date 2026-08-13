import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Alert, Button, Card, Field, Loading, Page, TextArea } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import {
  type CompanyProfile,
  getOwnCompanyProfile,
  saveCompanyProfile,
} from "../companies/client";

export interface CompanyProfileRouteProps {
  principal?: MeResponse | null;
}

interface Form {
  display_name: string;
  about: string;
  website: string;
  locations: string;
  benefits: string;
}

const EMPTY: Form = { display_name: "", about: "", website: "", locations: "", benefits: "" };

function toForm(profile: CompanyProfile | null | undefined): Form {
  if (profile === null || profile === undefined) return EMPTY;
  return {
    display_name: profile.display_name,
    about: profile.about,
    website: profile.website ?? "",
    locations: profile.locations.join(", "),
    benefits: profile.benefits.join(", "),
  };
}

/** Kommagetrennt: eine Zeile, die man einfügen kann. Leeres fällt weg. */
function parseList(raw: string): string[] {
  return raw
    .split(",")
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
}

export function CompanyProfileRoute({ principal = null }: CompanyProfileRouteProps) {
  const queryClient = useQueryClient();
  const tenantId = principal?.tenant_id ?? null;

  const query = useQuery({
    queryKey: ["company", "profile", "me", tenantId],
    queryFn: getOwnCompanyProfile,
    enabled: tenantId !== null,
  });

  const [form, setForm] = useState<Form>(EMPTY);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  // Nur ein GELUNGENER Abruf füllt das Formular. Vorher gab der Client bei
  // jedem Fehlschlag `null`, `toForm(null)` ist EMPTY, und die Seite sah aus wie
  // „noch nichts eingetragen" — wer dann den Namen tippte und speicherte,
  // überschrieb Über-uns, Website, Standorte und Benefits mit leer.
  const loaded = query.data?.ok === true ? query.data.profile : undefined;
  useEffect(() => {
    if (loaded !== undefined) setForm(toForm(loaded));
  }, [loaded]);

  const save = useMutation({
    mutationFn: () =>
      saveCompanyProfile({
        display_name: form.display_name,
        about: form.about,
        // Leer heißt „kein Link", nicht „leerer Link": ein leerer String würde
        // gerendert und führte ins Nichts.
        website: form.website.trim() === "" ? null : form.website.trim(),
        locations: parseList(form.locations),
        benefits: parseList(form.benefits),
      }),
    onSuccess: (result) => {
      if (result.ok) {
        setError(null);
        setSaved(true);
        queryClient.setQueryData(["company", "profile", "me", tenantId], result.profile);
      } else {
        setSaved(false);
        setError(result.message);
      }
    },
  });

  if (tenantId === null) {
    return (
      <Page title="Unser Unternehmen" narrow>
        <Card>
          <p>
            Wähle oben ein Unternehmen — oder lass dich von jemandem aus deinem Unternehmen
            einladen.
          </p>
        </Card>
      </Page>
    );
  }

  if (query.isPending) {
    return (
      <Page title="Unser Unternehmen" narrow>
        <Card>
          <Loading label="Profil wird geladen…" />
        </Card>
      </Page>
    );
  }

  // Kein Formular, wenn der Abruf scheiterte: man kann nicht bearbeiten, was man
  // nicht lesen konnte, und ein leeres Formular wäre die Einladung, echte Daten
  // zu überschreiben.
  if (query.data?.ok === false) {
    return (
      <Page title="Unser Unternehmen" narrow>
        <Card>
          <Alert>
            {query.data.message} Bearbeiten lässt sich das Profil erst wieder, wenn es lesbar
            ist — sonst würdet ihr überschreiben, was gerade niemand sehen kann.
          </Alert>
        </Card>
      </Page>
    );
  }

  function update<K extends keyof Form>(key: K, value: Form[K]) {
    setSaved(false);
    setForm((current) => ({ ...current, [key]: value }));
  }

  return (
    <Page
      title="Unser Unternehmen"
      narrow
      lead="Das sehen Bewerber neben jeder eurer Stellen. Solange hier nichts steht, bleibt eine Ausschreibung anonym — Titel und Beschreibung, sonst nichts."
    >

      <Card>
        <form
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate();
          }}
        >
          <Field
            label="Anzeigename"
            hint="Wie ihr auftretet — nicht zwingend der Name aus dem Handelsregister."
            value={form.display_name}
            onChange={(e) => update("display_name", e.target.value)}
            maxLength={160}
            required
          />
          <TextArea
            label="Über uns"
            hint="Wer ihr seid und woran ihr arbeitet."
            rows={8}
            value={form.about}
            onChange={(e) => update("about", e.target.value)}
            maxLength={8000}
          />
          <Field
            label="Website"
            type="url"
            hint="Optional, und nur http oder https."
            placeholder="https://…"
            value={form.website}
            onChange={(e) => update("website", e.target.value)}
          />
          <Field
            label="Standorte"
            hint="Mit Komma getrennt, zum Beispiel: Berlin, Hamburg"
            value={form.locations}
            onChange={(e) => update("locations", e.target.value)}
          />
          <Field
            label="Leistungen"
            hint="Mit Komma getrennt, zum Beispiel: Homeoffice, Weiterbildung"
            value={form.benefits}
            onChange={(e) => update("benefits", e.target.value)}
          />

          {error !== null ? <Alert>{error}</Alert> : null}
          {/* `notice` und nicht `error`: eine Bestätigung, die den Vorleser
              unterbricht, ist Lärm. */}
          {saved && error === null ? (
            <Alert variant="notice">Profil gespeichert.</Alert>
          ) : null}

          <Button type="submit" disabled={save.isPending}>
            {save.isPending ? "Wird gespeichert…" : "Speichern"}
          </Button>
        </form>
      </Card>
    </Page>
  );
}
