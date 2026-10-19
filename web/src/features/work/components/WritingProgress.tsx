import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Box from "@mui/material/Box";
import LinearProgress from "@mui/material/LinearProgress";
import Typography from "@mui/material/Typography";

/**
 * Progress while the model writes.
 *
 * The seconds count from the server-side start (`since`), not from the mount.
 * Otherwise the number jumps to 0 on every navigation although the service has
 * kept writing.
 */
export function WritingProgress({ since }: { since?: string | null }) {
  const { t } = useTranslation();
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, []);

  const start = since ? Date.parse(since) : Number.NaN;
  const seconds = Number.isFinite(start) ? Math.max(0, Math.floor((now - start) / 1000)) : 0;

  return (
    <Box sx={{ mt: 1.5 }}>
      <LinearProgress sx={{ mb: 1 }} />
      <Typography variant="body2">{t("drafts.writingNow")}</Typography>
      {seconds > 0 ? (
        <Typography variant="body2" color="text.secondary">
          {t("drafts.writingSince", { seconds })}
        </Typography>
      ) : null}
    </Box>
  );
}
