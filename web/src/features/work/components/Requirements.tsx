import Box from "@mui/material/Box";
import { Trans, useTranslation } from "react-i18next";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { matchSkills } from "../lib/match";

/**
 * Visible to screen readers only.
 *
 * Stands here and not in `shared/`: this pass was not allowed to touch
 * `shared/`. No colour value, no literal — pure geometry.
 */
function ScreenReaderOnly({ children }: { children: React.ReactNode }) {
  return (
    <Box
      component="span"
      sx={{
        position: "absolute",
        width: 1,
        height: 1,
        p: 0,
        m: -1,
        overflow: "hidden",
        clip: "rect(0 0 0 0)",
        whiteSpace: "nowrap",
        border: 0,
      }}
    >
      {children}
    </Box>
  );
}

/**
 * What the job asks for — and, when a profile is there, which of it one has.
 *
 * The fit is shown to the PERSON, not to the company, and it orders jobs, not
 * people. It is computed here in the browser: so it exists nowhere as a record
 * that somebody evaluates later.
 *
 * <strong>No percentage.</strong> A number looks like a measurement and hides
 * what counts — which skill is missing. The list says it, and with it the
 * person knows what they could do (ADR-0022).
 *
 * Lives here and not in a page because it stands on TWO: in the result list it
 * helps with sorting out, on the application page with phrasing — there one
 * sees which skill is missing while typing the cover letter. On NO page under
 * `/company/**` and in no candidate card.
 *
 * Three states, and the distinction carries:
 * - `mine === null` — not signed in or the answer is pending: no fit line at
 *   all, instead of claiming a gap nobody knows.
 * - `mine === []` — signed in, answer there, nothing entered: a sentence about
 *   the profile, **never "0 of 3"**. The person said nothing, not that they can
 *   do nothing.
 * - otherwise — compare.
 */
export function Requirements({
  skills,
  mine,
}: {
  skills: string[];
  mine: string[] | null;
}) {
  const { t } = useTranslation();
  // The same preparation as in the comparison, so that the displayed list and
  // the compared one are the same. If they diverged, an entry would stand there
  // that can never become a tick.
  const listed = skills
    .map((skill) => skill.trim())
    .filter((skill) => skill !== "");

  // Nothing named: then there is nothing to compare either. A "0 of 0" would be
  // a statement about a job that made none.
  if (listed.length === 0) return null;

  const match =
    mine === null || mine.length === 0 ? null : matchSkills(listed, mine);
  const have = new Set(match?.have ?? []);

  return (
    <Box sx={{ mt: 1.5 }}>
      {match !== null ? (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 0.75 }}>
          {t("jobs.youHave", {
            treffer: match.have.length,
            gesamt: listed.length,
          })}
        </Typography>
      ) : null}
      {mine !== null && mine.length === 0 ? (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 0.75 }}>
          <Trans
            i18nKey="jobs.noSkills"
            components={{ 1: <RouterLink to="/profile" /> }}
          />
        </Typography>
      ) : null}
      <Box
        component="ul"
        sx={{
          listStyle: "none",
          display: "flex",
          flexWrap: "wrap",
          gap: 1,
          p: 0,
          m: 0,
        }}
      >
        {listed.map((skill) => {
          const state =
            match === null ? "unknown" : have.has(skill) ? "have" : "missing";
          return (
            <Box
              component="li"
              key={skill}
              data-match={state}
              sx={{
                px: 1.25,
                py: 0.375,
                borderRadius: 999,
                border: 1,
                fontSize: "0.875rem",
                // Green and red are reserved for granted/revoked and must not
                // appear decoratively here. A tick is no judgement about a human
                // being — it therefore carries only the ordinary text colour,
                // and the symbol makes the difference.
                borderColor: state === "have" ? "text.primary" : "divider",
                color: state === "missing" ? "text.secondary" : "text.primary",
              }}
            >
              {state !== "unknown" ? (
                <Box
                  component="span"
                  // The symbol is decoration; the word below carries it for a
                  // screen reader, which would otherwise hear three names and no
                  // difference.
                  aria-hidden="true"
                >
                  {state === "have" ? "✓ " : "✗ "}
                </Box>
              ) : null}
              <Box component="span">{skill}</Box>
              {state !== "unknown" ? (
                <ScreenReaderOnly>
                  {t(state === "have" ? "jobs.have" : "jobs.missing")}
                </ScreenReaderOnly>
              ) : null}
            </Box>
          );
        })}
      </Box>
    </Box>
  );
}
