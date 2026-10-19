import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { failureKey } from "../../../shared/api/errors";
import { openInitiative } from "../api/messages";

/**
 * "Apply without an advert" on a company's careers page (VISION-FRAGEN B2).
 *
 * <strong>The release is said before the button</strong>: sending opens her
 * profile to this company, and only to it. Taking it back under /consents
 * closes it again.
 */
export function InitiativeBlock({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [text, setText] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState<string | null>(null);

  return (
    <Card sx={{ mb: 3 }}>
      <CardContent>
        <Typography variant="h2" sx={{ mb: 1 }}>
          {t("initiative.title")}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          {t("initiative.lead")}
        </Typography>

        {sent !== null ? (
          <Alert severity="success">
            {t("initiative.sent")}{" "}
            <Link component={RouterLink} to={`/messages?kind=initiative&reference=${sent}`}>
              {t("scout.approachOpenConversation")}
            </Link>
          </Alert>
        ) : !open ? (
          <Button variant="outlined" onClick={() => setOpen(true)}>
            {t("initiative.start")}
          </Button>
        ) : (
          <Box sx={{ display: "grid", gap: 1.5 }}>
            <TextField
              label={t("initiative.text")}
              multiline
              minRows={5}
              value={text}
              onChange={(event) => setText(event.target.value)}
              slotProps={{ htmlInput: { maxLength: 4000 } }}
            />
            <Alert severity="info">{t("initiative.release")}</Alert>
            {error !== null ? <Alert severity="error">{error}</Alert> : null}
            <Box sx={{ display: "flex", gap: 1.5 }}>
              <Button
                variant="contained"
                disabled={busy || text.trim() === ""}
                onClick={() => {
                  setBusy(true);
                  void openInitiative(companyId, text).then((result) => {
                    setBusy(false);
                    if (result.ok) setSent(result.reference);
                    else setError(t(failureKey(result.error, "errors.messageNotSent")));
                  });
                }}
              >
                {t("initiative.send")}
              </Button>
              <Button variant="text" onClick={() => setOpen(false)}>
                {t("messages.discard")}
              </Button>
            </Box>
          </Box>
        )}
      </CardContent>
    </Card>
  );
}
