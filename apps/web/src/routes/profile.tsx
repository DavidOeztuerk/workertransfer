import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import {
  Alert,
  Button,
  Card,
  Checkbox,
  Field,
  Loading,
  Page,
  Switch,
  TextArea,
} from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import {
  type Profile,
  getMyProfile,
  getVisibility,
  saveMyProfile,
  setVisibility,
} from "../profile/client";
import { DraftHelp } from "../profile/DraftHelp";
import { parseSkills } from "../skills";

export interface ProfileRouteProps {
  // Injizierbar, damit der Test einen Prinzipal rendern kann, ohne eine
  // laufende Sitzung zu brauchen — dasselbe Muster wie in den anderen Routen.
  principal?: MeResponse | null;
}

interface FormState {
  headline: string;
  bio: string;
  location: string;
  remote_ok: boolean;
  skills: string;
}

const EMPTY: FormState = { headline: "", bio: "", location: "", remote_ok: false, skills: "" };

function toForm(profile: Profile | null): FormState {
  if (profile === null) return EMPTY;
  return {
    headline: profile.headline,
    bio: profile.bio,
    location: profile.location,
    remote_ok: profile.remote_ok,
    skills: profile.skills.join(", "),
  };
}

export function ProfileRoute({ principal = null }: ProfileRouteProps) {
  const queryClient = useQueryClient();
  const subjectId = principal?.user_id ?? null;

  const profileQuery = useQuery({
    queryKey: ["profile", "me"],
    queryFn: getMyProfile,
    enabled: subjectId !== null,
  });

  const visibilityQuery = useQuery({
    queryKey: ["profile", "visibility", subjectId],
    queryFn: () => getVisibility(subjectId as string),
    enabled: subjectId !== null,
  });

  const [form, setForm] = useState<FormState>(EMPTY);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  // Das Formular folgt dem Server, solange niemand tippt. `data` wechselt nur,
  // wenn die Abfrage neu auflöst — danach gehört der Zustand der Person.
  const loaded = profileQuery.data;
  useEffect(() => {
    if (loaded !== undefined) setForm(toForm(loaded));
  }, [loaded]);

  const save = useMutation({
    mutationFn: () =>
      saveMyProfile({
        headline: form.headline,
        bio: form.bio,
        location: form.location,
        remote_ok: form.remote_ok,
        skills: parseSkills(form.skills),
      }),
    onSuccess: (result) => {
      if (result.ok) {
        setError(null);
        setSaved(true);
        queryClient.setQueryData(["profile", "me"], result.profile);
      } else {
        setSaved(false);
        setError(result.message);
      }
    },
  });

  const toggle = useMutation({
    mutationFn: (next: boolean) => setVisibility(subjectId as string, next),
    onSuccess: (result, next) => {
      if (result.ok) {
        setError(null);
        // Der Ledger sagt, was gilt — nicht der Wunsch des Klicks.
        queryClient.setQueryData(["profile", "visibility", subjectId], result.granted);
      } else {
        setError(result.message);
        // Zurückstellen: ein Schalter, der „sichtbar" zeigt, obwohl nichts
        // freigegeben wurde, wäre die gefährlichere Lüge.
        queryClient.setQueryData(["profile", "visibility", subjectId], !next);
      }
    },
  });

  if (subjectId === null) {
    return (
      <Page title="Mein Profil" narrow>
        <Card>
          <p>
            Bitte <a href="/login">anmelden</a>, um dein Profil zu bearbeiten.
          </p>
        </Card>
      </Page>
    );
  }

  if (profileQuery.isPending) {
    // Kein leeres Formular, das sich nachträglich füllt: wer in der Zwischenzeit
    // zu tippen anfängt, verliert seine Eingabe, sobald die Antwort eintrifft.
    return (
      <Page title="Mein Profil" narrow>
        <Card>
          <Loading label="Profil wird geladen…" />
        </Card>
      </Page>
    );
  }

  const hasProfile = profileQuery.data != null;
  // Die ANZEIGE bleibt bei Nichtwissen aus — ein Schalter, der versehentlich
  // „freigegeben" behauptet, ist die gefährlichere Lüge.
  const released = visibilityQuery.data === true;
  // ... aber „weiß ich nicht" ist nicht „nein". Solange die Antwort aussteht
  // oder ausbleibt, darf der Schalter nicht BEDIENBAR sein: sonst schickt der
  // nächste Klick ein `grant` für eine Einwilligung, deren Zustand niemand
  // kennt. `null` kommt aus `isGranted` und heißt „der Ledger hat nicht
  // geantwortet" (vorher war das von „nicht freigegeben" nicht zu
  // unterscheiden).
  const ledgerSilent = !visibilityQuery.isPending && visibilityQuery.data === null;
  const ledgerUnknown = visibilityQuery.isPending || ledgerSilent;

  function update<K extends keyof FormState>(key: K, value: FormState[K]) {
    setSaved(false);
    setForm((current) => ({ ...current, [key]: value }));
  }

  return (
    <Page
      title="Mein Profil"
      narrow
      lead="Was hier steht, sieht zunächst niemand. Sichtbar wird es erst, wenn du es freigibst — und unsichtbar in dem Moment, in dem du die Freigabe zurückziehst."
    >

      <Card className="profile__release">
        <Switch
          label="Profil für Unternehmen freigeben"
          checked={released}
          disabled={!hasProfile || toggle.isPending || ledgerUnknown}
          hint={
            ledgerSilent
              ? "Ob eine Freigabe gilt, ist gerade nicht abrufbar. Solange das so ist, ändert dieser Schalter nichts — sonst würdest du etwas freigeben, dessen Stand niemand kennt."
              : visibilityQuery.isPending
                ? "Freigabe wird geprüft…"
                : hasProfile
                  ? "Wirkt sofort. Ein Widerruf entzieht den Zugriff, ohne dass du jemanden darum bitten musst."
                  : "Erst ein Profil speichern — freigeben lässt sich nur, was es gibt."
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
          <Field
            label="Überschrift"
            hint="Eine Zeile, die sagt, worum es dir geht."
            value={form.headline}
            onChange={(e) => update("headline", e.target.value)}
            maxLength={120}
            required
          />
          <TextArea
            label="Über mich"
            hint="Freitext. Was ein Lebenslauf nicht hergibt."
            rows={6}
            value={form.bio}
            onChange={(e) => update("bio", e.target.value)}
            maxLength={4000}
          />
          <DraftHelp
            onDraft={(draft) => update("bio", draft)}
            hasText={form.bio.trim().length > 0}
          />
          <Field
            label="Ort"
            value={form.location}
            onChange={(e) => update("location", e.target.value)}
            maxLength={120}
          />
          <Field
            label="Fähigkeiten"
            // Der Hinweis erklärt, warum aus „postgres" nach dem Speichern
            // „PostgreSQL" wird. Ohne ihn sähe es aus, als hätte die Seite
            // etwas an der Eingabe verändert, ohne zu fragen.
            hint="Mit Komma getrennt, zum Beispiel: Python, FastAPI, PostgreSQL. Bekannte Schreibweisen vereinheitlichen wir — aus „postgres“ wird „PostgreSQL“. Was wir nicht kennen, bleibt genau so stehen."
            value={form.skills}
            onChange={(e) => update("skills", e.target.value)}
          />
          {/* Das Bauteil, nicht nur seine Klasse: handgebaut fehlten
              `.wt-checkbox__box` und `.wt-checkbox__label`, also bekam das
              Kästchen weder seine Größe noch `accent-color` und der Text kein
              `cursor: pointer`. Das war die letzte solche Stelle. */}
          <Checkbox
            label="Remote-Arbeit kommt für mich in Frage"
            checked={form.remote_ok}
            onChange={(e) => update("remote_ok", e.target.checked)}
          />

          {error !== null ? <Alert>{error}</Alert> : null}
          {/* `notice` und nicht `error`: eine Bestätigung, die den Vorleser
              unterbricht, ist Lärm. */}
          {saved && error === null ? <Alert variant="notice">Profil gespeichert.</Alert> : null}

          <Button type="submit" disabled={save.isPending}>
            {save.isPending ? "Wird gespeichert…" : "Speichern"}
          </Button>
        </form>
      </Card>
    </Page>
  );
}
