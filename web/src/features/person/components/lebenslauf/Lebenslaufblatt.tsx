import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import Box from "@mui/material/Box";
import Chip from "@mui/material/Chip";
import Typography from "@mui/material/Typography";

export interface LebenslaufPosition {
  employer: string;
  title: string;
  started_on: string;
  ended_on: string | null;
  description: string;
  technologies: string[];
}

export interface LebenslaufAusbildung {
  institution: string;
  qualification: string;
  started_on: string;
  ended_on: string | null;
  kind?: "schule" | "ausbildung";
}

export type Lebenslaufvorlage = "schlicht" | "klassisch" | "modern";

export interface Lebenslaufdaten {
  headline: string;
  bio: string;
  location: string;
  skills: string[];
  positions: LebenslaufPosition[];
  education: LebenslaufAusbildung[];
}

/**
 * Eine Komponente, drei Vorlagen — für die Person und das Unternehmen dieselbe.
 *
 * Nur Tokens, keine Farbliterale. Druckbar, damit ein PDF über den Browser
 * entsteht; der Server erzeugt keins (ADR-0035).
 */
export function Lebenslaufblatt({
  daten,
  template,
}: {
  daten: Lebenslaufdaten;
  template: Lebenslaufvorlage;
}) {
  const { t, i18n } = useTranslation();

  function monat(wert: string | null): string {
    if (wert === null || wert === "") return t("lebenslauf.heute");
    const datum = new Date(`${wert}-01`);
    if (Number.isNaN(datum.getTime())) return wert;
    return datum.toLocaleDateString(i18n.language, { month: "short", year: "numeric" });
  }

  const kopf = (
    <Box sx={{ mb: 3, pb: 2, borderBottom: 2, borderColor: "divider" }}>
      <Typography variant="h3" sx={{ mb: 0.5, fontWeight: 700 }}>
        {daten.headline || t("lebenslauf.keineUeberschrift")}
      </Typography>
      {daten.location ? (
        <Typography variant="body2" color="text.secondary">
          {daten.location}
        </Typography>
      ) : (
        <Typography variant="body2" color="text.secondary">
          {t("lebenslauf.ortFehlt")}
        </Typography>
      )}
    </Box>
  );

  const bio = daten.bio ? (
    <Abschnitt titel={t("lebenslauf.ueberMich")} template={template}>
      <Typography sx={{ whiteSpace: "pre-wrap" }}>{daten.bio}</Typography>
    </Abschnitt>
  ) : null;

  const faehigkeiten =
    daten.skills.length > 0 ? (
      <Abschnitt titel={t("lebenslauf.faehigkeiten")} template={template}>
        <Box sx={{ display: "flex", flexWrap: "wrap", gap: 0.5 }}>
          {daten.skills.map((skill) => (
            <Chip key={skill} label={skill} size="small" variant="outlined" />
          ))}
        </Box>
      </Abschnitt>
    ) : (
      <Abschnitt titel={t("lebenslauf.faehigkeiten")} template={template}>
        <Typography variant="body2" color="text.secondary">
          {t("lebenslauf.faehigkeitenFehlen")}
        </Typography>
      </Abschnitt>
    );

  const stationen =
    daten.positions.length > 0 ? (
      <Abschnitt titel={t("lebenslauf.stationen")} template={template}>
        {daten.positions.map((pos, i) => (
          <Box key={i} sx={{ mb: 2 }}>
            <Typography variant="h6" sx={{ fontWeight: 600 }}>
              {pos.title}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {pos.employer} · {monat(pos.started_on)} – {monat(pos.ended_on)}
            </Typography>
            {pos.description ? (
              <Typography sx={{ mt: 0.5, whiteSpace: "pre-wrap" }}>{pos.description}</Typography>
            ) : null}
            {pos.technologies.length > 0 ? (
              <Box sx={{ mt: 0.75, display: "flex", flexWrap: "wrap", gap: 0.5 }}>
                {pos.technologies.map((tech) => (
                  <Chip key={tech} label={tech} size="small" variant="outlined" />
                ))}
              </Box>
            ) : null}
          </Box>
        ))}
      </Abschnitt>
    ) : (
      <Abschnitt titel={t("lebenslauf.stationen")} template={template}>
        <Typography variant="body2" color="text.secondary">
          {t("lebenslauf.stationenFehlen")}
        </Typography>
      </Abschnitt>
    );

  const lehren = daten.education.filter((edu) => edu.kind !== "schule");
  const schulen = daten.education.filter((edu) => edu.kind === "schule");

  const ausbildung =
    lehren.length > 0 ? (
      <Abschnitt titel={t("lebenslauf.ausbildung")} template={template}>
        {lehren.map((edu, i) => (
          <Box key={i} sx={{ mb: 1.5 }}>
            <Typography variant="h6" sx={{ fontWeight: 600 }}>
              {edu.qualification || edu.institution}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {edu.institution} · {monat(edu.started_on)} – {monat(edu.ended_on)}
            </Typography>
          </Box>
        ))}
      </Abschnitt>
    ) : (
      <Abschnitt titel={t("lebenslauf.ausbildung")} template={template}>
        <Typography variant="body2" color="text.secondary">
          {t("lebenslauf.ausbildungFehlt")}
        </Typography>
      </Abschnitt>
    );

  const schule =
    schulen.length > 0 ? (
      <Abschnitt titel={t("lebenslauf.schule")} template={template}>
        {schulen.map((edu, i) => (
          <Box key={i} sx={{ mb: 1.5 }}>
            <Typography variant="h6" sx={{ fontWeight: 600 }}>
              {edu.qualification || edu.institution}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {edu.institution} · {monat(edu.started_on)} – {monat(edu.ended_on)}
            </Typography>
          </Box>
        ))}
      </Abschnitt>
    ) : null;

  const inhalt =
    template === "klassisch" ? (
      <>
        {kopf}
        {bio}
        <Box
          sx={{
            display: "grid",
            gap: 3,
            gridTemplateColumns: { xs: "1fr", sm: "minmax(0, 2fr) minmax(0, 1fr)" },
          }}
        >
          <Box>
            {stationen}
            {ausbildung}
            {schule}
          </Box>
          <Box>{faehigkeiten}</Box>
        </Box>
      </>
    ) : (
      <>
        {kopf}
        {bio}
        {stationen}
        {ausbildung}
        {schule}
        {faehigkeiten}
      </>
    );

  return (
    <Box
      sx={{
        maxWidth: "21cm",
        mx: "auto",
        px: { xs: 3, sm: template === "modern" ? 6 : 5 },
        py: { xs: 3, sm: template === "modern" ? 7 : 5 },
        bgcolor: "background.paper",
        ...(template === "modern"
          ? { "& h3, & h6": { letterSpacing: 0.4, textTransform: "uppercase" } }
          : {}),
        "@media print": {
          maxWidth: "none",
          px: 0,
          py: 0,
        },
      }}
    >
      {inhalt}
    </Box>
  );
}

function Abschnitt({
  titel,
  template,
  children,
}: {
  titel: string;
  template: Lebenslaufvorlage;
  children: ReactNode;
}) {
  return (
    <Box sx={{ mb: template === "modern" ? 4 : 3 }}>
      <Typography
        variant="h6"
        sx={{
          mb: 1.5,
          fontWeight: template === "modern" ? 800 : 700,
          borderBottom: 1,
          borderColor: "divider",
          pb: 0.5,
        }}
      >
        {titel}
      </Typography>
      {children}
    </Box>
  );
}
