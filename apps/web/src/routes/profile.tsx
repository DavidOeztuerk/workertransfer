import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { Button, Card, Field, Switch, TextArea } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import {
  type Profile,
  draftProfileText,
  getMyProfile,
  getVisibility,
  saveMyProfile,
  setVisibility,
} from "../profile/client";
import { parseSkills } from "../skills";

export interface ProfileRouteProps {
  // Injizierbar, damit der Test einen Prinzipal rendern kann, ohne eine
  // laufende Sitzung zu brauchen — wie bei CompanyNewRoute.
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
      <main className="page page--narrow">
        <Card>
          <h1>Mein Profil</h1>
          <p>
            Bitte <a href="/login">anmelden</a>, um dein Profil zu bearbeiten.
          </p>
        </Card>
      </main>
    );
  }

  if (profileQuery.isPending) {
    // Kein leeres Formular, das sich nachträglich füllt: wer in der Zwischenzeit
    // zu tippen anfängt, verliert seine Eingabe, sobald die Antwort eintrifft.
    return (
      <main className="page page--narrow">
        <Card>
          <p role="status">Profil wird geladen…</p>
        </Card>
      </main>
    );
  }

  const hasProfile = profileQuery.data != null;
  const released = visibilityQuery.data === true;

  function update<K extends keyof FormState>(key: K, value: FormState[K]) {
    setSaved(false);
    setForm((current) => ({ ...current, [key]: value }));
  }

  return (
    <main className="page page--narrow">
      <header className="page__header">
        <h1>Mein Profil</h1>
        <p className="page__lead">
          Was hier steht, sieht zunächst niemand. Sichtbar wird es erst, wenn du es freigibst — und
          unsichtbar in dem Moment, in dem du die Freigabe zurückziehst.
        </p>
      </header>

      <Card className="profile__release">
        <Switch
          label="Profil für Unternehmen freigeben"
          checked={released}
          disabled={!hasProfile || toggle.isPending}
          hint={
            hasProfile
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
          <label className="wt-checkbox">
            <input
              type="checkbox"
              checked={form.remote_ok}
              onChange={(e) => update("remote_ok", e.target.checked)}
            />
            <span>Remote-Arbeit kommt für mich in Frage</span>
          </label>

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

/**
 * Formulierungshilfe — auf Anforderung, und sie sagt, was hinausgeht.
 *
 * Die drei Entscheidungen, die hier sichtbar sind:
 *
 * 1. **Nur auf Knopfdruck.** Kein Vorschlag von selbst, kein Hintergrundlauf.
 *    Dieselbe Regel wie bei GitHub: einmal auf Bitte hinsehen ist etwas anderes
 *    als dauerhaft hinterhersehen.
 * 2. **Der Hinweis steht am Knopf**, nicht in einer Datenschutzerklärung. Wer
 *    drückt, hat gelesen, dass sein Text an einen fremden Anbieter geht.
 * 3. **Der Entwurf ersetzt nur das Formularfeld.** Gespeichert wird er erst,
 *    wenn die Person auf „Speichern" drückt — dann ist es ihr Text.
 *
 * Und ein bewusstes Detail: hat sie schon etwas geschrieben, heißt der Knopf
 * „umformulieren" statt „schreiben". Ein Knopf, der ungefragt vorhandene
 * Arbeit überschreibt, wird einmal gedrückt und danach nie wieder.
 */
function DraftHelp({
  onDraft,
  hasText,
}: {
  onDraft: (draft: string) => void;
  hasText: boolean;
}) {
  const [wish, setWish] = useState("");
  const [problem, setProblem] = useState<string | null>(null);

  const ask = useMutation({
    mutationFn: () => draftProfileText(wish),
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
        label={hasText ? "Text umformulieren lassen" : "Beim Schreiben helfen lassen"}
        hint="Optional: was dir wichtig ist („kürzer“, „sachlicher“, „ich bin Pflegefachkraft“). Dein Profiltext und deine Fähigkeiten gehen dafür an Anthropic. Name und Adresse nicht. Gespeichert wird nichts — der Vorschlag landet nur im Feld oben, und du entscheidest."
        value={wish}
        onChange={(e) => setWish(e.target.value)}
        maxLength={200}
      />
      {problem !== null ? (
        <p className="auth__alert" role="alert">
          {problem}
        </p>
      ) : null}
      {/* type="button" ausgeschrieben, obwohl `Button` es ohnehin so vorgibt:
          dieser Knopf steht im selben <form> wie „Speichern“, und wer hier
          liest, soll nicht erst das UI-Paket aufschlagen müssen, um zu wissen,
          welcher der beiden absendet. */}
      <Button type="button" variant="quiet" onClick={() => ask.mutate()} disabled={ask.isPending}>
        {ask.isPending
          ? "Wird geschrieben…"
          : hasText
            ? "Vorschlag holen (ersetzt den Text oben)"
            : "Vorschlag holen"}
      </Button>
    </div>
  );
}
