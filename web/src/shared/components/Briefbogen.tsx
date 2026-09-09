import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";

/**
 * Ein Anschreiben, gesetzt wie ein Brief — ohne eine Datei zu sein.
 *
 * Dieselbe Komponente für die Prüfansicht und die Unternehmensmappe. Zwei
 * Darstellungen wären zwei Wahrheiten.
 */
export interface Briefkopf {
  name?: string;
  zeilen?: string[];
}

function zeilenVon(kopf?: Briefkopf): string[] {
  return [kopf?.name, ...(kopf?.zeilen ?? [])].filter(
    (zeile): zeile is string => typeof zeile === "string" && zeile.trim().length > 0,
  );
}

function Block({ zeilen, sx }: { zeilen: string[]; sx?: object }) {
  if (zeilen.length === 0) return null;
  return (
    <Box sx={{ mb: 3, textAlign: "left", ...sx }}>
      {zeilen.map((zeile, i) => (
        <Typography
          key={`${i}-${zeile}`}
          sx={{ fontFamily: "inherit", fontSize: "11pt", lineHeight: 1.5 }}
        >
          {zeile}
        </Typography>
      ))}
    </Box>
  );
}

export function Briefbogen({
  betreff,
  text,
  leerHinweis,
  kopf,
  empfaenger,
}: {
  betreff?: string;
  text: string;
  /** Steht im Brief, wenn noch kein Text da ist — nie ein Platzhalter-Unternehmen. */
  leerHinweis?: string;
  /** Absender. Konto oder Snapshot, nie das Modell. */
  kopf?: Briefkopf;
  /** Empfänger. Firmenprofil und Stelle, nie das Modell. */
  empfaenger?: Briefkopf;
}) {
  const absätze = text.length === 0 ? [] : text.split(/\n{2,}/);
  const absender = zeilenVon(kopf);
  const an = zeilenVon(empfaenger);

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
      <Block zeilen={absender} />
      <Block zeilen={an} sx={{ mb: 4 }} />
      {betreff ? (
        <Typography
          component="h2"
          sx={{
            fontFamily: "inherit",
            fontSize: "14pt",
            fontWeight: 600,
            mb: 3,
          }}
        >
          {betreff}
        </Typography>
      ) : null}
      {absätze.length === 0 ? (
        <Typography sx={{ fontFamily: "inherit", fontStyle: "italic", color: "text.secondary" }}>
          {leerHinweis ?? "\u00a0"}
        </Typography>
      ) : (
        absätze.map((absatz, i) => (
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
            {absatz}
          </Typography>
        ))
      )}
    </Box>
  );
}
