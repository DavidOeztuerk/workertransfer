import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Chip from "@mui/material/Chip";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";
import { useTranslation } from "react-i18next";

import {
  belegartenFuer,
  hochladbareBelege,
} from "../../../shared/lib/berufsfelder";
import type { BerufsfeldWahl } from "../../../shared/lib/berufsfelder";

/**
 * Welche Nachweise zu dieser Arbeit gehören (ADR-0039).
 *
 * <strong>Vorschläge, keine Vorschrift.</strong> Was hier steht, ordnet für den
 * Menschen, der die Seite liest — es entscheidet nicht, was hochgeladen werden
 * DARF. Ein Entwickler mit einem Meisterbrief kann ihn ablegen; ein
 * Metallbauer, der ein Repositorium hat, kann es verbinden. Eine Liste
 * erlaubter Nachweise wäre eine Behauptung darüber, welche Arbeit es gibt.
 *
 * <strong>Erscheint nur, wenn ein Berufsfeld genannt wurde.</strong> Wer nichts
 * gewählt hat, sieht die heutige Ansicht — ein leerer oder ausgegrauter Kasten
 * wäre die stillschweigende Behauptung, dort fehle etwas (ADR-0022 §3).
 *
 * Kein Beleg wird durch diesen Kasten zu einer Nennung: hochgeladene Unterlagen
 * bleiben „belegt" und sind für keine Suche sichtbar (ADR-0033). Die Chips sind
 * deshalb auch NICHT anklickbar — anders als die Fähigkeitsvorschläge darüber,
 * die ins Formular wandern.
 */
export function Nachweise({ feld }: { feld: BerufsfeldWahl }) {
  const { t } = useTranslation();

  if (feld === null) return null;

  const alle = belegartenFuer(feld);
  const hochladbar = new Set(hochladbareBelege(feld).map((art) => art.schluessel));
  const nurGenannt = alle.filter((art) => !hochladbar.has(art.schluessel));

  return (
    <Card sx={{ mt: 3 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1 }}>
          {t("beleg.titel")}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          {t("beleg.lead")}
        </Typography>

        <Box sx={{ display: "flex", flexWrap: "wrap", gap: 0.75, mb: 2.5 }}>
          {alle.map((art) => (
            <Chip
              key={art.schluessel}
              label={t(art.schluessel)}
              size="small"
              variant="outlined"
            />
          ))}
        </Box>

        {/* Was NICHT hochgeladen werden kann, steht auch da — und mit Grund.
            Eine Liste, die eine Lücke verschweigt, sieht vollständig aus und
            ist es nicht. */}
        {nurGenannt.length > 0 ? (
          <Alert severity="info" sx={{ mb: 2.5 }}>
            {t("beleg.nurGenannt", {
              belege: nurGenannt.map((art) => t(art.schluessel)).join(", "),
            })}
          </Alert>
        ) : null}

        <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
          <Button component={RouterLink} to="/resume" variant="outlined">
            {t("beleg.zuUnterlagen")}
          </Button>
          <Button component={RouterLink} to="/portfolio" variant="outlined">
            {t("beleg.zuArbeiten")}
          </Button>
        </Box>
      </CardContent>
    </Card>
  );
}
