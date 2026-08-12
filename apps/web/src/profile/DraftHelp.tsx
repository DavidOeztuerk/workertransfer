import { useMutation } from "@tanstack/react-query";
import { useState } from "react";
import { Alert, Button, Field } from "@workertransfer/ui";

import { draftProfileText } from "./client";

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
export function DraftHelp({
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
      {problem !== null ? <Alert>{problem}</Alert> : null}
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
