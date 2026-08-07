import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Button, Card, Field, TextArea } from "@workertransfer/ui";

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

  const loaded = query.data;
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
      <main className="page page--narrow">
        <Card>
          <h1>Unser Unternehmen</h1>
          <p>
            Wähle oben ein Unternehmen — oder <a href="/company/new">lege eines an</a>.
          </p>
        </Card>
      </main>
    );
  }

  if (query.isPending) {
    return (
      <main className="page page--narrow">
        <Card>
          <p role="status">Profil wird geladen…</p>
        </Card>
      </main>
    );
  }

  function update<K extends keyof Form>(key: K, value: Form[K]) {
    setSaved(false);
    setForm((current) => ({ ...current, [key]: value }));
  }

  return (
    <main className="page page--narrow">
      <header className="page__header">
        <h1>Unser Unternehmen</h1>
        <p className="page__lead">
          Das sehen Bewerber neben jeder eurer Stellen. Solange hier nichts steht, bleibt eine
          Ausschreibung anonym — Titel und Beschreibung, sonst nichts.
        </p>
      </header>

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

          {error !== null ? (
            <p className="auth__alert" role="alert">
              {error}
            </p>
          ) : null}
          {saved && error === null ? <p className="page__note">Profil gespeichert.</p> : null}

          <Button type="submit" disabled={save.isPending}>
            {save.isPending ? "Wird gespeichert…" : "Speichern"}
          </Button>
        </form>
      </Card>
    </main>
  );
}
