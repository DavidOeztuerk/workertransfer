import Alert from "@mui/material/Alert";
import { useTranslation } from "react-i18next";
import AlertTitle from "@mui/material/AlertTitle";
import Box from "@mui/material/Box";
import CircularProgress from "@mui/material/CircularProgress";
import Typography from "@mui/material/Typography";

import type { ApiError } from "../../../core/store/thunkHelpers";

/** While loading. With text, so that a screen reader announces it. */
export function LoadingBlock({ label }: { label?: string }) {
  const { t } = useTranslation();

  return (
    <Box sx={{ display: "flex", gap: 1.5, alignItems: "center", py: 4 }} role="status">
      <CircularProgress size={18} />
      <Typography color="text.secondary">{label ?? t("common.loading")}</Typography>
    </Box>
  );
}

/**
 * When something went wrong.
 *
 * The correlation id stands small below — it is the only thread along which a
 * complaint can be traced back through every service. Whoever leaves it out
 * forces the person to guess the point in time.
 */
export function ErrorBlock({ error, title }: { error: ApiError; title?: string }) {
  const { t } = useTranslation();

  return (
    <Alert severity="error" sx={{ my: 2 }}>
      <AlertTitle>{title ?? error.title}</AlertTitle>
      {error.detail}
      {error.correlationId ? (
        <Box
          component="p"
          sx={{ mt: 1, mb: 0, fontSize: "0.8125rem", opacity: 0.8, fontFamily: "monospace" }}
        >
          {t("common.reference")}: {error.correlationId}
        </Box>
      ) : null}
    </Alert>
  );
}

/**
 * When there is nothing to show.
 *
 * "Empty" and "not released" are NOT the same, and this component must never
 * blur that: whoever sees nothing because nobody released anything must read
 * a different sentence than somebody who really created nothing.
 */
export function EmptyBlock({
  title,
  hint,
  action,
}: {
  title: string;
  hint?: string;
  action?: React.ReactNode;
}) {
  return (
    <Box
      sx={{
        display: "flex",
        flexDirection: "column",
        gap: 1.5,
        alignItems: "center",
        py: { xs: 5, md: 7 },
        px: 3,
        textAlign: "center",
        border: 1,
        borderColor: "divider",
        borderRadius: 2,
        borderStyle: "dashed",
      }}
    >
      <Typography variant="h3">{title}</Typography>
      {hint ? (
        <Typography color="text.secondary" sx={{ maxWidth: "48ch" }}>
          {hint}
        </Typography>
      ) : null}
      {action}
    </Box>
  );
}
