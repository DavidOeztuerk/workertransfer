import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";

/**
 * A cover letter, set like a letter — without being a file.
 *
 * The same component for the review view and the company's folder. Two
 * renderings would be two truths.
 */
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
}: {
  subject?: string;
  text: string;
  /** Stands in the letter while there is no text yet — never a placeholder company. */
  emptyHint?: string;
  /** The sender. Account or snapshot, never the model. */
  sender?: LetterHead;
  /** The recipient. Company profile and job, never the model. */
  recipient?: LetterHead;
}) {
  const paragraphs = text.length === 0 ? [] : text.split(/\n{2,}/);
  const from = linesOf(sender);
  const to = linesOf(recipient);

  return (
    <Box
      sx={{
        maxWidth: "21cm",
        mx: "auto",
        px: { xs: 3, sm: 6 },
        py: { xs: 4, sm: 7 },
        bgcolor: "transparent",
        color: "text.primary",
        border: 1,
        borderColor: "divider",
        fontFamily: 'Georgia, Cambria, "Times New Roman", serif',
        fontSize: "12pt",
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
      <Block lines={from} />
      <Block lines={to} sx={{ mb: 4 }} />
      {subject ? (
        <Typography
          component="h2"
          sx={{
            fontFamily: "inherit",
            fontSize: "14pt",
            fontWeight: 600,
            mb: 3,
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
