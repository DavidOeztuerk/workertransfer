import { useState } from "react";
import { useTranslation } from "react-i18next";
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
  const { t } = useTranslation();
  const [wish, setzeWunsch] = useState("");
  const [problem, setzeProblem] = useState<string | null>(null);
  const [running, setzeLaeuft] = useState(false);

  async function frage() {
    setzeLaeuft(true);
    const result = await draftProfileText(wish);
    setzeLaeuft(false);
    if (result.ok) {
      setzeProblem(null);
      onDraft(result.draft);
    } else {
      setzeProblem(result.message);
    }
  }

  return (
    <Box sx={{ display: "flex", flexDirection: "column", gap: 1.5 }}>
      <TextField
        label={t(hasText ? "entwurf.labelUmformulieren" : "entwurf.labelNeu")}
        helperText={t("entwurf.hinweis")}
        value={wish}
        onChange={(event) => setzeWunsch(event.target.value)}
        slotProps={{ htmlInput: { maxLength: 200 } }}
        fullWidth
      />
      {problem !== null ? <Alert severity="error">{problem}</Alert> : null}
      {/* `type="button"` ausgeschrieben: dieser Knopf steht im selben <form> wie
          „Speichern", und ohne die Angabe würde er das Profil absenden, statt zu
          helfen. */}
      <Box>
        <Button
          type="button"
          variant="text"
          onClick={() => void frage()}
          disabled={running}
        >
          {running
            ? t("entwurf.laeuft")
            : t(hasText ? "entwurf.knopfErsetzt" : "entwurf.knopfNeu")}
        </Button>
      </Box>
    </Box>
  );
}
