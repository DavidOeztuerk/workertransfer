import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import Box from "@mui/material/Box";
import Chip from "@mui/material/Chip";
import Typography from "@mui/material/Typography";

export interface CurriculumVitaePosition {
  employer: string;
  title: string;
  started_on: string;
  ended_on: string | null;
  description: string;
  technologies: string[];
}

export interface CurriculumVitaeEducation {
  institution: string;
  qualification: string;
  started_on: string;
  ended_on: string | null;
  kind?: "schule" | "ausbildung";
}

/** The wire words of the three templates. */
export type CurriculumVitaeTemplateName = "schlicht" | "klassisch" | "modern";

export interface CurriculumVitaeData {
  headline: string;
  bio: string;
  location: string;
  skills: string[];
  positions: CurriculumVitaePosition[];
  education: CurriculumVitaeEducation[];
}

/**
 * One component, three templates — the same for the person and the company.
 *
 * Tokens only, no colour literals. Printable, so that a PDF comes out of the
 * browser; the server renders none (ADR-0035).
 */
export function CurriculumVitaeSheet({
  data,
  template,
}: {
  data: CurriculumVitaeData;
  template: CurriculumVitaeTemplateName;
}) {
  const { t, i18n } = useTranslation();

  function month(value: string | null): string {
    if (value === null || value === "") return t("resume.today");
    const date = new Date(`${value}-01`);
    if (Number.isNaN(date.getTime())) return value;
    return date.toLocaleDateString(i18n.language, { month: "short", year: "numeric" });
  }

  const header = (
    <Box sx={{ mb: 3, pb: 2, borderBottom: 2, borderColor: "divider" }}>
      <Typography variant="h3" sx={{ mb: 0.5, fontWeight: 700 }}>
        {data.headline || t("resume.noHeadline")}
      </Typography>
      {data.location ? (
        <Typography variant="body2" color="text.secondary">
          {data.location}
        </Typography>
      ) : (
        <Typography variant="body2" color="text.secondary">
          {t("resume.locationMissing")}
        </Typography>
      )}
    </Box>
  );

  const bio = data.bio ? (
    <Section title={t("resume.aboutMe")} template={template}>
      <Typography sx={{ whiteSpace: "pre-wrap" }}>{data.bio}</Typography>
    </Section>
  ) : null;

  const skills =
    data.skills.length > 0 ? (
      <Section title={t("resume.skills")} template={template}>
        <Box sx={{ display: "flex", flexWrap: "wrap", gap: 0.5 }}>
          {data.skills.map((skill) => (
            <Chip key={skill} label={skill} size="small" variant="outlined" />
          ))}
        </Box>
      </Section>
    ) : (
      <Section title={t("resume.skills")} template={template}>
        <Typography variant="body2" color="text.secondary">
          {t("resume.skillsMissing")}
        </Typography>
      </Section>
    );

  const positions =
    data.positions.length > 0 ? (
      <Section title={t("resume.experience")} template={template}>
        {data.positions.map((pos, i) => (
          <Box key={i} sx={{ mb: 2 }}>
            <Typography variant="h6" sx={{ fontWeight: 600 }}>
              {pos.title}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {pos.employer} · {month(pos.started_on)} – {month(pos.ended_on)}
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
      </Section>
    ) : (
      <Section title={t("resume.experience")} template={template}>
        <Typography variant="body2" color="text.secondary">
          {t("resume.experienceMissing")}
        </Typography>
      </Section>
    );

  const apprenticeships = data.education.filter((edu) => edu.kind !== "schule");
  const schools = data.education.filter((edu) => edu.kind === "schule");

  const training =
    apprenticeships.length > 0 ? (
      <Section title={t("resume.training")} template={template}>
        {apprenticeships.map((edu, i) => (
          <Box key={i} sx={{ mb: 1.5 }}>
            <Typography variant="h6" sx={{ fontWeight: 600 }}>
              {edu.qualification || edu.institution}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {edu.institution} · {month(edu.started_on)} – {month(edu.ended_on)}
            </Typography>
          </Box>
        ))}
      </Section>
    ) : (
      <Section title={t("resume.training")} template={template}>
        <Typography variant="body2" color="text.secondary">
          {t("resume.trainingMissing")}
        </Typography>
      </Section>
    );

  const school =
    schools.length > 0 ? (
      <Section title={t("resume.school")} template={template}>
        {schools.map((edu, i) => (
          <Box key={i} sx={{ mb: 1.5 }}>
            <Typography variant="h6" sx={{ fontWeight: 600 }}>
              {edu.qualification || edu.institution}
            </Typography>
            <Typography variant="body2" color="text.secondary">
              {edu.institution} · {month(edu.started_on)} – {month(edu.ended_on)}
            </Typography>
          </Box>
        ))}
      </Section>
    ) : null;

  const content =
    template === "klassisch" ? (
      <>
        {header}
        {bio}
        <Box
          sx={{
            display: "grid",
            gap: 3,
            gridTemplateColumns: { xs: "1fr", sm: "minmax(0, 2fr) minmax(0, 1fr)" },
          }}
        >
          <Box>
            {positions}
            {training}
            {school}
          </Box>
          <Box>{skills}</Box>
        </Box>
      </>
    ) : (
      <>
        {header}
        {bio}
        {positions}
        {training}
        {school}
        {skills}
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
      {content}
    </Box>
  );
}

function Section({
  title,
  template,
  children,
}: {
  title: string;
  template: CurriculumVitaeTemplateName;
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
        {title}
      </Typography>
      {children}
    </Box>
  );
}
