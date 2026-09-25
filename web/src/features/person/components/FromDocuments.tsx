import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Typography from "@mui/material/Typography";

import type { DocumentTerms } from "../api/resume";

/**
 * The fourth source: one's own uploaded documents.
 *
 * <strong>The button is the whole rule.</strong> ADR-0004 forbids scraping; a
 * nightly run over the document store would keep its letter and miss its
 * point — a platform that permanently watches a human does something different
 * from one that looks once when asked. So the service reads only when somebody
 * clicks here, and never at upload.
 *
 * <strong>And it says what it could NOT do.</strong> A photographed master
 * craftsman's certificate is a picture; without text recognition on images
 * there is nothing in it to read. Showing that as "no suggestions" would be the
 * silent claim that nothing stands in this certificate — ADR-0022 §3, a lie by
 * omission. It is therefore said explicitly, with the file's name.
 *
 * The words found appear <em>above</em> in the same list as those from GitHub,
 * the CV and one's own portfolio — one piece of evidence weighs like another,
 * and none is weighed against another (ADR-0039).
 */
export function FromDocuments({
  findings,
  reading,
  error,
  onRead,
}: {
  findings: readonly DocumentTerms[];
  reading: boolean;
  error: string | null;
  onRead: () => void;
}) {
  const { t } = useTranslation();

  // Whoever uploaded nothing does not see this section at all — neither an
  // empty one nor a greyed-out one. The same rule as for a missing GitHub
  // account: whoever has nothing is not worse, but elsewhere.
  if (findings.length === 0) return null;

  const read = findings.filter((finding) => finding.read_at !== null);
  const withoutText = read.filter((finding) => !finding.has_text);
  // "Read, nothing found" says nothing about the document: the vocabulary knows
  // what someone entered by pull request, not every trade there is (ADR-0023).
  const nothingFound =
    read.length > 0 && read.every((finding) => finding.terms.length === 0);

  return (
    <Box sx={{ mt: 1 }}>
      <Typography variant="subtitle2" sx={{ mb: 0.5 }}>
        {t("profile.documentsTitle")}
      </Typography>
      <Typography
        variant="caption"
        color="text.secondary"
        sx={{ display: "block", mb: 1 }}
      >
        {t("profile.documentsHint")}
      </Typography>

      <Button
        variant="outlined"
        size="small"
        onClick={onRead}
        disabled={reading}
      >
        {t(reading ? "profile.documentsReading" : "profile.documentsRead")}
      </Button>

      {error !== null ? (
        <Alert severity="error" sx={{ mt: 1 }}>
          {error}
        </Alert>
      ) : null}

      {nothingFound && withoutText.length < read.length ? (
        <Typography
          variant="caption"
          color="text.secondary"
          sx={{ display: "block", mt: 1 }}
        >
          {t("profile.documentsNothingFound")}
        </Typography>
      ) : null}

      {withoutText.map((finding) => (
        <Typography
          key={finding.document_id}
          variant="caption"
          color="text.secondary"
          sx={{ display: "block", mt: 1 }}
        >
          {t("profile.documentsNoText", { name: finding.name })}
        </Typography>
      ))}
    </Box>
  );
}
