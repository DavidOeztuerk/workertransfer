import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Typography from "@mui/material/Typography";

import type { MessageKey } from "../../../shared/api/errors";
import { CloudCancelled, type CloudSource, cloudSources, pickFromDropbox, pickFromGoogle } from "../lib/cloudFiles";

const LABELS: Record<CloudSource, MessageKey> = {
  google: "cloudFiles.google",
  dropbox: "cloudFiles.dropbox",
};

/**
 * "From Google Drive" / "From Dropbox" beside a file input (ADR-0054).
 *
 * <strong>Chosen is not stored.</strong> The file lands in the same form as
 * one from the disk — named, sorted, and only "Upload" files it. Nothing here
 * is drawn without keys, and the providers' scripts load on the click.
 */
export function CloudFiles({
  extensions,
  disabled,
  onFile,
}: {
  extensions: string[];
  disabled?: boolean;
  onFile: (file: File) => void;
}) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState<CloudSource | null>(null);
  const [failed, setFailed] = useState(false);
  const sources = cloudSources();

  if (sources.length === 0) return null;

  async function pick(source: CloudSource) {
    setBusy(source);
    setFailed(false);
    try {
      const file = await (source === "google" ? pickFromGoogle(extensions) : pickFromDropbox(extensions));
      onFile(file);
    } catch (error) {
      if (!(error instanceof CloudCancelled)) setFailed(true);
    } finally {
      setBusy(null);
    }
  }

  return (
    <Box sx={{ display: "grid", gap: 1 }}>
      <Box sx={{ display: "flex", gap: 1, flexWrap: "wrap" }}>
        {sources.map((source) => (
          <Button
            key={source}
            variant="text"
            size="small"
            disabled={disabled || busy !== null}
            onClick={() => void pick(source)}
          >
            {busy === source ? t("cloudFiles.fetching") : t(LABELS[source])}
          </Button>
        ))}
      </Box>
      <Typography variant="caption" color="text.secondary">
        {t("cloudFiles.hint")}
      </Typography>
      {failed ? <Alert severity="error">{t("cloudFiles.failed")}</Alert> : null}
    </Box>
  );
}
