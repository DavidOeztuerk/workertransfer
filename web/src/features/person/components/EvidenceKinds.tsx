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
 * Which kinds of evidence belong to this work (ADR-0039).
 *
 * Suggestions, not a prescription — anything can be uploaded. Appears only
 * with a named occupational field; an empty box would be the claim that
 * something is missing there (ADR-0022 §3). The chips are not clickable: a
 * piece of evidence does not become a naming that way (ADR-0033).
 */
export function EvidenceKinds({ field }: { field: OccupationalFieldChoice }) {
  const { t } = useTranslation();

  if (field === null) return null;

  const all = evidenceKindsFor(field);
  const uploadable = new Set(uploadableEvidenceKinds(field).map((kind) => kind.labelKey));
  const nameOnly = all.filter((kind) => !uploadable.has(kind.labelKey));

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
          {all.map((kind) => (
            <Chip
              key={kind.labelKey}
              label={t(kind.labelKey)}
              size="small"
              variant="outlined"
            />
          ))}
        </Box>

        {nameOnly.length > 0 ? (
          <Alert severity="info" sx={{ mb: 2.5 }}>
            {t("credentials.nameOnly", {
              belege: nameOnly.map((kind) => t(kind.labelKey)).join(", "),
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
