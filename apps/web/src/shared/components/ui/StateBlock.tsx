import Alert from "@mui/material/Alert";
import AlertTitle from "@mui/material/AlertTitle";
import Box from "@mui/material/Box";
import CircularProgress from "@mui/material/CircularProgress";
import Typography from "@mui/material/Typography";

import type { ApiError } from "../../../core/store/thunkHelpers";

/** Während geladen wird. Mit Text, damit ein Screenreader es ansagt. */
export function LoadingBlock({ label = "Wird geladen …" }: { label?: string }) {
  return (
    <Box sx={{ display: "flex", gap: 1.5, alignItems: "center", py: 4 }} role="status">
      <CircularProgress size={18} />
      <Typography color="text.secondary">{label}</Typography>
    </Box>
  );
}

/**
 * Wenn etwas schiefging.
 *
 * Die Korrelationskennung steht klein darunter — sie ist der einzige Faden, an
 * dem sich eine Beschwerde durch alle Dienste zurückverfolgen lässt. Wer sie
 * weglässt, zwingt die Person, den Zeitpunkt zu schätzen.
 */
export function ErrorBlock({ error, title }: { error: ApiError; title?: string }) {
  return (
    <Alert severity="error" sx={{ my: 2 }}>
      <AlertTitle>{title ?? error.title}</AlertTitle>
      {error.detail}
      {error.correlationId ? (
        <Box
          component="p"
          sx={{ mt: 1, mb: 0, fontSize: "0.8125rem", opacity: 0.8, fontFamily: "monospace" }}
        >
          Kennung: {error.correlationId}
        </Box>
      ) : null}
    </Alert>
  );
}

/**
 * Wenn es nichts zu zeigen gibt.
 *
 * „Leer" und „nicht freigegeben" sind NICHT dasselbe, und diese Komponente darf
 * das nie verwischen: wer nichts sieht, weil niemand freigegeben hat, muss
 * einen anderen Satz lesen als jemand, der wirklich nichts angelegt hat.
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
