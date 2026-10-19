import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";

/**
 * A cover letter, set like a letter — without being a file.
 *
 * The same component for the review view and the company's folder. Two
 * renderings would be two truths.
 *
 * <strong>It wears the CV's template</strong> (VISION-FRAGEN-2 P2): the same
 * three names, chosen once on the CV page. A folder whose letter and CV look
 * like two people wrote them reads worse than either alone.
 */
export type LetterTemplate = "schlicht" | "klassisch" | "modern";

const TYPE: Record<LetterTemplate, { font: string; size: string; subject: object; sender: object; sheet: object }> = {
  schlicht: {
    font: "inherit",
    size: "11pt",
    subject: { fontSize: "13pt", fontWeight: 600 },
    sender: {},
    sheet: {},
  },
  klassisch: {
    font: 'Georgia, Cambria, "Times New Roman", serif',
    size: "12pt",
    subject: { fontSize: "14pt", fontWeight: 600 },
    sender: {},
    sheet: {},
  },
  modern: {
    font: "inherit",
    size: "11pt",
    subject: { fontSize: "12pt", fontWeight: 700, letterSpacing: "0.06em", textTransform: "uppercase", color: "primary.main" },
    sender: { "& > :first-of-type": { fontWeight: 700, letterSpacing: "0.08em", textTransform: "uppercase" } },
    sheet: { borderTop: 4, borderTopColor: "primary.main" },
  },
};
export interface LetterHead {
  name?: string;
  lines?: string[];
}

function linesOf(head?: LetterHead): string[] {
  return [head?.name, ...(head?.lines ?? [])].filter(
    (line): line is string => typeof line === "string" && line.trim().length > 0,
  );
}

function Block({ lines, sx }: { lines: string[]; sx?: object }) {
  if (lines.length === 0) return null;
  return (
    <Box sx={{ mb: 3, textAlign: "left", ...sx }}>
      {lines.map((line, i) => (
        <Typography
          key={`${i}-${line}`}
          sx={{ fontFamily: "inherit", fontSize: "11pt", lineHeight: 1.5 }}
        >
          {line}
        </Typography>
      ))}
    </Box>
  );
}

export function LetterSheet({
  subject,
  text,
  emptyHint,
  sender,
  recipient,
  template = "schlicht",
}: {
  subject?: string;
  text: string;
  /** Stands in the letter while there is no text yet — never a placeholder company. */
  emptyHint?: string;
  /** The sender. Account or snapshot, never the model. */
  sender?: LetterHead;
  /** The recipient. Company profile and job, never the model. */
  recipient?: LetterHead;
  /** The CV's template; without a CV its default. */
  template?: LetterTemplate;
}) {
  const paragraphs = text.length === 0 ? [] : text.split(/\n{2,}/);
  const from = linesOf(sender);
  const to = linesOf(recipient);

  return (
    <Box
      data-template={template}
      sx={{
        maxWidth: "21cm",
        mx: "auto",
        px: { xs: 3, sm: 6 },
        py: { xs: 4, sm: 7 },
        bgcolor: "transparent",
        color: "text.primary",
        border: 1,
        borderColor: "divider",
        fontFamily: TYPE[template].font,
        fontSize: TYPE[template].size,
        ...TYPE[template].sheet,
        lineHeight: 1.7,
        "@media print": {
          border: 0,
          boxShadow: "none",
          maxWidth: "none",
          px: 0,
          py: 0,
        },
      }}
    >
      <Block lines={from} sx={TYPE[template].sender} />
      <Block lines={to} sx={{ mb: 4 }} />
      {subject ? (
        <Typography
          component="h2"
          sx={{
            fontFamily: "inherit",
            mb: 3,
            ...TYPE[template].subject,
          }}
        >
          {subject}
        </Typography>
      ) : null}
      {paragraphs.length === 0 ? (
        <Typography sx={{ fontFamily: "inherit", fontStyle: "italic", color: "text.secondary" }}>
          {emptyHint ?? " "}
        </Typography>
      ) : (
        paragraphs.map((paragraph, i) => (
          <Typography
            key={i}
            component="p"
            sx={{
              fontFamily: "inherit",
              mb: 2,
              textAlign: "justify",
              whiteSpace: "pre-wrap",
            }}
          >
            {paragraph}
          </Typography>
        ))
      )}
    </Box>
  );
}
