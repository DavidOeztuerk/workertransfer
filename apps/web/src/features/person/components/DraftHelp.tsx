import { useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import TextField from "@mui/material/TextField";

import { draftProfileText } from "../api/profile";

/**
 * Formulierungshilfe — auf Anforderung, und sie sagt, was hinausgeht.
 *
 * Die drei Entscheidungen, die hier sichtbar sind:
 *
 * 1. <strong>Nur auf Knopfdruck.</strong> Kein Vorschlag von selbst, kein
 *    Hintergrundlauf, kein Gedächtnis. Dieselbe Regel wie bei GitHub: einmal
 *    auf Bitte hinsehen ist etwas anderes als dauerhaft hinterhersehen.
 * 2. <strong>Der Hinweis steht am Knopf</strong>, nicht in einer
 *    Datenschutzerklärung. Wer drückt, hat gelesen, dass sein Text an einen
 *    fremden Anbieter geht.
 * 3. <strong>Der Entwurf ersetzt nur das Formularfeld.</strong> Gespeichert
 *    wird er erst, wenn die Person auf „Speichern" drückt — dann ist es ihr
 *    Text. Es gibt keinen Eintrag im Ledger: der Knopf IST die Einwilligung,
 *    informiert und je Benutzung.
 *
 * Und ein bewusstes Detail: hat sie schon etwas geschrieben, heisst der Knopf
 * „umformulieren" statt „schreiben". Ein Knopf, der ungefragt vorhandene Arbeit
 * überschreibt, wird einmal gedrückt und danach nie wieder.
 */
export function DraftHelp({
  onDraft,
  hasText,
}: {
  onDraft: (draft: string) => void;
  hasText: boolean;
}) {
  const [wunsch, setzeWunsch] = useState("");
  const [problem, setzeProblem] = useState<string | null>(null);
  const [laeuft, setzeLaeuft] = useState(false);

  async function frage() {
    setzeLaeuft(true);
    const ergebnis = await draftProfileText(wunsch);
    setzeLaeuft(false);
    if (ergebnis.ok) {
      setzeProblem(null);
      onDraft(ergebnis.draft);
    } else {
      setzeProblem(ergebnis.message);
    }
  }

  return (
    <Box sx={{ display: "flex", flexDirection: "column", gap: 1.5 }}>
      <TextField
        label={hasText ? "Text umformulieren lassen" : "Beim Schreiben helfen lassen"}
        helperText="Optional: was dir wichtig ist („kürzer“, „sachlicher“, „ich bin Pflegefachkraft“). Dein Profiltext und deine Fähigkeiten gehen dafür an Anthropic. Name und Adresse nicht. Gespeichert wird nichts — der Vorschlag landet nur im Feld oben, und du entscheidest."
        value={wunsch}
        onChange={(ereignis) => setzeWunsch(ereignis.target.value)}
        slotProps={{ htmlInput: { maxLength: 200 } }}
        fullWidth
      />
      {problem !== null ? <Alert severity="error">{problem}</Alert> : null}
      {/* `type="button"` ausgeschrieben: dieser Knopf steht im selben <form> wie
          „Speichern", und ohne die Angabe würde er das Profil absenden, statt zu
          helfen. */}
      <Box>
        <Button type="button" variant="text" onClick={() => void frage()} disabled={laeuft}>
          {laeuft
            ? "Wird geschrieben…"
            : hasText
              ? "Vorschlag holen (ersetzt den Text oben)"
              : "Vorschlag holen"}
        </Button>
      </Box>
    </Box>
  );
}
