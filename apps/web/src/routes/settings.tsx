import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Card, Switch } from "@workertransfer/ui";

import type { MeResponse } from "../auth/client";
import {
  ALL_ON,
  type NotificationPreferences,
  getNotificationPreferences,
  saveNotificationPreferences,
} from "../settings/client";

export interface SettingsRouteProps {
  principal?: MeResponse | null;
}

const SWITCHES: { key: keyof NotificationPreferences; label: string; hint: string }[] = [
  {
    key: "market_request",
    label: "Wenn ein Unternehmen deinen Marktstatus sehen möchte",
    hint: "Ohne diese Nachricht erfährst du davon nur, wenn du zufällig vorbeischaust.",
  },
  {
    key: "resume_request",
    label: "Wenn ein Unternehmen nach deinem Lebenslauf fragt",
    hint: "Auch hier entscheidest du — aber nur, wenn du von der Frage weißt.",
  },
  {
    key: "transfer_update",
    label: "Wenn sich bei einem Gespräch etwas tut",
    hint: "Interesse, Angebot, Rückzug.",
  },
  {
    key: "application_update",
    label: "Wenn sich bei einer Bewerbung etwas tut",
    hint: "Nur Züge des Unternehmens — deine eigenen kennst du.",
  },
];

/**
 * Jeder Schalter wirkt sofort, es gibt keinen Speichern-Knopf.
 *
 * Das ist keine Bequemlichkeit, sondern die Zusage des Bauteils: `Switch` ist
 * ein `button[role="switch"]` und kein Formularfeld, gerade weil eine Checkbox
 * verspricht, die Änderung gelte erst beim Absenden. Ein Speichern-Knopf
 * darunter würde genau dieses Versprechen wieder einführen — und wer die Seite
 * vorher verlässt, hätte sich nicht abgemeldet, sondern nur geglaubt, es getan
 * zu haben.
 */
export function SettingsRoute({ principal = null }: SettingsRouteProps) {
  const queryClient = useQueryClient();
  const subjectId = principal?.user_id ?? null;
  const [error, setError] = useState<string | null>(null);

  const query = useQuery({
    queryKey: ["notification-preferences"],
    queryFn: getNotificationPreferences,
    enabled: subjectId !== null,
  });

  const save = useMutation({
    mutationFn: (next: NotificationPreferences) => saveNotificationPreferences(next),
    onSuccess: (result) => {
      if (result.ok) {
        setError(null);
        queryClient.setQueryData(["notification-preferences"], result.preferences);
      } else {
        setError(result.message);
        // Den geglaubten Zustand verwerfen: der Schalter soll zeigen, was gilt,
        // nicht was gewollt war.
        void queryClient.invalidateQueries({ queryKey: ["notification-preferences"] });
      }
    },
  });

  if (subjectId === null) {
    return (
      <main className="page page--narrow">
        <Card>
          <h1>Einstellungen</h1>
          <p>
            Bitte <a href="/login">anmelden</a>, um deine Einstellungen zu ändern.
          </p>
        </Card>
      </main>
    );
  }

  const values = query.data ?? ALL_ON;

  return (
    <main className="page page--narrow">
      <header className="page__header">
        <h1>Einstellungen</h1>
        <p className="page__lead">
          Was in einer Mail steht, ist bewusst wenig:{" "}
          <strong>„Es gibt etwas Neues für dich."</strong> Kein Firmenname, kein Vorgang, keine
          Anzahl. Eine Mail kann in einem Postfach landen, das nicht nur dir gehört — und dann wäre
          der Satz, der sie nützlicher machte, genau der, der dich den Arbeitsplatz kostet. Was es
          ist, steht hinter der Anmeldung.
        </p>
      </header>

      <Card>
        <h2>Benachrichtigungen</h2>

        {error !== null ? (
          <p className="auth__alert" role="alert">
            {error}
          </p>
        ) : null}

        {SWITCHES.map((entry) => (
          <Switch
            key={entry.key}
            label={entry.label}
            hint={entry.hint}
            checked={values[entry.key]}
            disabled={save.isPending || query.isPending}
            onChange={(next) => save.mutate({ ...values, [entry.key]: next })}
          />
        ))}

        <p className="page__note">
          Höchstens eine Mail pro Stunde, egal wie viel passiert — auch der Zeitpunkt einer Mail
          verrät etwas.
        </p>
      </Card>
    </main>
  );
}
