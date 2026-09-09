import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import Box from "@mui/material/Box";
import LinearProgress from "@mui/material/LinearProgress";
import Typography from "@mui/material/Typography";

/**
 * Fortschritt während das Modell schreibt.
 *
 * Die Sekunden zählen ab dem serverseitigen Start (`seit`), nicht ab dem
 * Mount. Sonst springt die Zahl bei jedem Navigieren auf 0, obwohl der
 * Dienst weitergeschrieben hat.
 */
export function Schreibfortschritt({ seit }: { seit?: string | null }) {
  const { t } = useTranslation();
  const [jetzt, setJetzt] = useState(() => Date.now());

  useEffect(() => {
    const id = window.setInterval(() => setJetzt(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, []);

  const start = seit ? Date.parse(seit) : Number.NaN;
  const sekunden = Number.isFinite(start)
    ? Math.max(0, Math.floor((jetzt - start) / 1000))
    : 0;

  return (
    <Box sx={{ mt: 1.5 }}>
      <LinearProgress sx={{ mb: 1 }} />
      <Typography variant="body2">{t("entwurfs.schreibtJetzt")}</Typography>
      {sekunden > 0 ? (
        <Typography variant="body2" color="text.secondary">
          {t("entwurfs.schreibtSeit", { seconds: sekunden })}
        </Typography>
      ) : null}
    </Box>
  );
}
