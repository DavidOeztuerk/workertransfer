import { useState } from "react";
import { useTranslation } from "react-i18next";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Checkbox from "@mui/material/Checkbox";
import FormControl from "@mui/material/FormControl";
import FormControlLabel from "@mui/material/FormControlLabel";
import FormLabel from "@mui/material/FormLabel";
import Link from "@mui/material/Link";
import MenuItem from "@mui/material/MenuItem";
import Radio from "@mui/material/Radio";
import RadioGroup from "@mui/material/RadioGroup";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

type Kind = "final" | "interim";

/** Why an interim reference is asked for — written out, never built from the value. */
const REASON_KEY = {
  none: "referenceLetter.reasonNone",
  manager: "referenceLetter.reasonManager",
  tasks: "referenceLetter.reasonTasks",
  training: "referenceLetter.reasonTraining",
} as const;

type Reason = keyof typeof REASON_KEY;

const REASONS = Object.keys(REASON_KEY) as Reason[];

const SENTENCE_KEY = {
  none: "referenceLetter.sentenceNone",
  manager: "referenceLetter.sentenceManager",
  tasks: "referenceLetter.sentenceTasks",
  training: "referenceLetter.sentenceTraining",
} as const satisfies Record<Reason, string>;

/**
 * "Zeugnis anfordern" (VISION-FRAGEN C2, ADR-0050) — a sample letter, filled
 * in the browser.
 *
 * <strong>A sample and not a model.</strong> A general letter for a right
 * everybody has is no examination of a single case (§ 2 RDG); a model
 * rewriting it for her situation would be one. Nothing is stored and nothing
 * is sent: she copies it and sends it herself — by mail, by post, or signed
 * electronically.
 *
 * <strong>The interim reference only after a tick.</strong> Asking for one
 * tells an employer that somebody may be looking around, and that risk is
 * hers to take knowingly (C2) — the letter appears only once she has said so.
 */
export function ReferenceLetter() {
  const { t, i18n } = useTranslation();
  const [kind, setKind] = useState<Kind>("final");
  const [aware, setAware] = useState(false);
  const [reason, setReason] = useState<Reason>("none");
  const [name, setName] = useState("");
  const [end, setEnd] = useState("");
  const [due, setDue] = useState("");
  const [copied, setCopied] = useState(false);

  const blank = (value: string, placeholder: string) => (value.trim() === "" ? placeholder : value.trim());
  const day = (value: string) =>
    value === "" ? t("referenceLetter.placeholderDate") : new Date(`${value}T00:00:00`).toLocaleDateString(i18n.language);

  const shown = kind === "final" || aware;
  const letter =
    kind === "final"
      ? t("referenceLetter.finalText", {
          end: day(end),
          due: day(due),
          name: blank(name, t("referenceLetter.placeholderName")),
        })
      : t("referenceLetter.interimText", {
          opening: t(SENTENCE_KEY[reason]),
          due: day(due),
          name: blank(name, t("referenceLetter.placeholderName")),
        });

  async function copy() {
    try {
      await navigator.clipboard.writeText(letter);
      setCopied(true);
    } catch {
      // Not every browser grants the clipboard; the text stands in the field
      // to be selected by hand.
      setCopied(false);
    }
  }

  return (
    <Card component="section" aria-label={t("referenceLetter.title")} sx={{ mb: 2.5 }}>
      <CardContent sx={{ display: "grid", gap: 2 }}>
        <Typography variant="h2">{t("referenceLetter.title")}</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ maxWidth: "74ch" }}>
          {t("referenceLetter.lead")}
        </Typography>

        <FormControl>
          <FormLabel>{t("referenceLetter.kindLabel")}</FormLabel>
          <RadioGroup
            value={kind}
            onChange={(event) => {
              setKind(event.target.value as Kind);
              setCopied(false);
            }}
          >
            <FormControlLabel value="final" control={<Radio />} label={t("referenceLetter.kindFinal")} />
            <FormControlLabel value="interim" control={<Radio />} label={t("referenceLetter.kindInterim")} />
          </RadioGroup>
        </FormControl>

        {kind === "interim" ? (
          <>
            <Alert severity="warning">{t("referenceLetter.interimRisk")}</Alert>
            <FormControlLabel
              control={<Checkbox checked={aware} onChange={(event) => setAware(event.target.checked)} />}
              label={t("referenceLetter.interimAware")}
            />
          </>
        ) : null}

        {shown ? (
          <>
            <Box sx={{ display: "grid", gap: 1.5, maxWidth: 420 }}>
              <TextField
                label={t("referenceLetter.name")}
                value={name}
                onChange={(event) => setName(event.target.value)}
              />
              {kind === "final" ? (
                <TextField
                  label={t("referenceLetter.end")}
                  type="date"
                  slotProps={{ inputLabel: { shrink: true } }}
                  value={end}
                  onChange={(event) => setEnd(event.target.value)}
                />
              ) : (
                <TextField
                  select
                  label={t("referenceLetter.reason")}
                  value={reason}
                  onChange={(event) => setReason(event.target.value as Reason)}
                >
                  {REASONS.map((option) => (
                    <MenuItem key={option} value={option}>
                      {t(REASON_KEY[option])}
                    </MenuItem>
                  ))}
                </TextField>
              )}
              <TextField
                label={t("referenceLetter.due")}
                type="date"
                slotProps={{ inputLabel: { shrink: true } }}
                value={due}
                onChange={(event) => setDue(event.target.value)}
              />
            </Box>

            <TextField
              label={t("referenceLetter.letter")}
              value={letter}
              multiline
              minRows={10}
              slotProps={{ htmlInput: { readOnly: true } }}
            />
            <Box sx={{ display: "flex", gap: 1.5, alignItems: "center", flexWrap: "wrap" }}>
              <Button variant="outlined" onClick={() => void copy()}>
                {t("referenceLetter.copy")}
              </Button>
              {copied ? (
                <Typography variant="body2" color="text.secondary">
                  {t("referenceLetter.copied")}
                </Typography>
              ) : null}
            </Box>
            <Typography variant="body2" color="text.secondary" sx={{ maxWidth: "74ch" }}>
              {t("referenceLetter.afterwards")}{" "}
              <Link component={RouterLink} to="/resume">
                {t("referenceLetter.toDocuments")}
              </Link>
            </Typography>
          </>
        ) : null}
      </CardContent>
    </Card>
  );
}
