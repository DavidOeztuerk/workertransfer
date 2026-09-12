import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Typography from "@mui/material/Typography";

import type { Unterlagenfund } from "../api/resume";

/**
 * Die vierte Quelle: die eigenen hochgeladenen Unterlagen.
 *
 * <strong>Der Knopf ist die ganze Regel.</strong> ADR-0004 verbietet Scraping;
 * ein Nachtlauf über die Ablage hielte seinen Buchstaben und verfehlte seinen
 * Sinn — eine Plattform, die einem Menschen dauerhaft hinterhersieht, tut etwas
 * anderes als eine, die einmal auf seine Bitte hinsieht. Deshalb liest der
 * Dienst erst, wenn hier jemand klickt, und nie beim Hochladen.
 *
 * <strong>Und sie sagt, was sie NICHT konnte.</strong> Ein abfotografierter
 * Meisterbrief ist ein Bild; ohne Texterkennung auf Bildern steht darin nichts
 * zu lesen. Das als „keine Vorschläge" zu zeigen wäre die stillschweigende
 * Behauptung, in diesem Meisterbrief stehe nichts — ADR-0022 §3, Lüge durch
 * Auslassung. Es steht deshalb ausdrücklich da, samt dem Namen der Datei.
 *
 * Die gefundenen Wörter selbst erscheinen <em>oben</em> in derselben Liste wie
 * die aus GitHub, dem Lebenslauf und den eigenen Arbeiten — ein Beleg wiegt
 * wie der andere, und keiner wird gegen einen anderen gewogen (ADR-0039).
 */
export function AusUnterlagen({
  funde,
  laeuft,
  fehler,
  onLesen,
}: {
  funde: readonly Unterlagenfund[];
  laeuft: boolean;
  fehler: string | null;
  onLesen: () => void;
}) {
  const { t } = useTranslation();

  // Wer nichts hochgeladen hat, sieht diesen Abschnitt gar nicht — auch keinen
  // leeren und keinen ausgegrauten. Dieselbe Regel wie beim fehlenden
  // GitHub-Konto: wer nichts hat, ist nicht schlechter, sondern woanders.
  if (funde.length === 0) return null;

  const gelesen = funde.filter((fund) => fund.read_at !== null);
  const ohneText = gelesen.filter((fund) => !fund.has_text);
  const nichtsGefunden =
    gelesen.length > 0 && gelesen.every((fund) => fund.terms.length === 0);

  return (
    <Box sx={{ mt: 1 }}>
      <Typography variant="subtitle2" sx={{ mb: 0.5 }}>
        {t("profil.unterlagenTitel")}
      </Typography>
      <Typography
        variant="caption"
        color="text.secondary"
        sx={{ display: "block", mb: 1 }}
      >
        {t("profil.unterlagenHinweis")}
      </Typography>

      <Button
        variant="outlined"
        size="small"
        onClick={onLesen}
        disabled={laeuft}
      >
        {t(laeuft ? "profil.unterlagenLiest" : "profil.unterlagenLesen")}
      </Button>

      {fehler !== null ? (
        <Alert severity="error" sx={{ mt: 1 }}>
          {fehler}
        </Alert>
      ) : null}

      {/*
        „Gelesen, nichts gefunden" — und ausdrücklich mit dem Nachsatz, dass das
        nichts über die Unterlage sagt. Der Wortschatz kennt, was jemand per
        Pull Request eingetragen hat; er ist keine Liste aller Arbeit, die es
        gibt (ADR-0023).
      */}
      {nichtsGefunden && ohneText.length < gelesen.length ? (
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ display: "block", mt: 1 }}
        >
          {t("profil.unterlagenNichtsGefunden")}
        </Typography>
      ) : null}

      {ohneText.map((fund) => (
        <Typography
          key={fund.document_id}
          variant="caption"
          color="text.secondary"
          sx={{ display: "block", mt: 1 }}
        >
          {t("profil.unterlagenOhneText", { name: fund.name })}
        </Typography>
      ))}
    </Box>
  );
}
