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
 * Vorschläge, keine Vorschrift — hochladen lässt sich alles. Erscheint nur mit
 * genanntem Berufsfeld; ein leerer Kasten wäre die Behauptung, dort fehle etwas
 * (ADR-0022 §3). Die Chips sind nicht anklickbar: ein Beleg wird dadurch keine
 * Nennung (ADR-0033).
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
