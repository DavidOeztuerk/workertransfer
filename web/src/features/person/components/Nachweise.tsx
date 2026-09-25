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
  evidenceKindsFor,
  uploadableEvidenceKinds,
} from "../../../shared/lib/occupationalFields";
import type { OccupationalFieldChoice } from "../../../shared/lib/occupationalFields";

/**
 * Welche Nachweise zu dieser Arbeit gehören (ADR-0039).
 *
 * Vorschläge, keine Vorschrift — hochladen lässt sich alles. Erscheint nur mit
 * genanntem Berufsfeld; ein leerer Kasten wäre die Behauptung, dort fehle etwas
 * (ADR-0022 §3). Die Chips sind nicht anklickbar: ein Beleg wird dadurch keine
 * Nennung (ADR-0033).
 */
export function Nachweise({ feld }: { feld: OccupationalFieldChoice }) {
  const { t } = useTranslation();

  if (feld === null) return null;

  const alle = evidenceKindsFor(feld);
  const hochladbar = new Set(uploadableEvidenceKinds(feld).map((art) => art.labelKey));
  const nurGenannt = alle.filter((art) => !hochladbar.has(art.labelKey));

  return (
    <Card sx={{ mt: 3 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1 }}>
          {t("credentials.title")}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          {t("credentials.lead")}
        </Typography>

        <Box sx={{ display: "flex", flexWrap: "wrap", gap: 0.75, mb: 2.5 }}>
          {alle.map((art) => (
            <Chip
              key={art.labelKey}
              label={t(art.labelKey)}
              size="small"
              variant="outlined"
            />
          ))}
        </Box>

        {nurGenannt.length > 0 ? (
          <Alert severity="info" sx={{ mb: 2.5 }}>
            {t("credentials.nameOnly", {
              belege: nurGenannt.map((art) => t(art.labelKey)).join(", "),
            })}
          </Alert>
        ) : null}

        <Box sx={{ display: "flex", gap: 1.5, flexWrap: "wrap" }}>
          <Button component={RouterLink} to="/resume" variant="outlined">
            {t("credentials.toDocuments")}
          </Button>
          <Button component={RouterLink} to="/portfolio" variant="outlined">
            {t("credentials.toPortfolio")}
          </Button>
        </Box>
      </CardContent>
    </Card>
  );
}
