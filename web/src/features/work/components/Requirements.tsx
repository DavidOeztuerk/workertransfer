import Box from "@mui/material/Box";
import Typography from "@mui/material/Typography";
import { Link as RouterLink } from "react-router-dom";

import { matchSkills } from "../lib/match";

/**
 * Nur für Vorlesegeräte sichtbar.
 *
 * Steht hier und nicht in `shared/`: dieser Durchgang darf `shared/` nicht
 * anfassen. Kein Farbwert, kein Literal — reine Geometrie.
 */
function NurVorgelesen({ children }: { children: React.ReactNode }) {
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
 * Was die Stelle verlangt — und, wenn ein Profil da ist, was davon man hat.
 *
 * Die Passung wird der PERSON gezeigt, nicht dem Unternehmen, und sie ordnet
 * Stellen, keine Menschen. Gerechnet wird hier im Browser: so gibt es sie
 * nirgends als Datensatz, den später jemand auswertet.
 *
 * <strong>Kein Prozentwert.</strong> Eine Zahl sieht aus wie eine Messung und
 * verschweigt, was zählt — welche Fähigkeit fehlt. Die Liste sagt es, und damit
 * weiß die Person, was sie tun könnte (ADR-0022).
 *
 * Liegt hier und nicht in einer Seite, weil sie auf ZWEI steht: in der
 * Trefferliste hilft sie beim Aussortieren, auf der Bewerbungsseite beim
 * Formulieren — dort sieht man, welche Fähigkeit fehlt, während man das
 * Anschreiben tippt. Auf KEINER Seite unter `/company/**` und in keiner
 * Kandidatenkarte.
 *
 * Drei Zustände, und die Unterscheidung trägt:
 * - `mine === null` — nicht angemeldet oder die Antwort steht aus: gar keine
 *   Passungszeile, statt eine Lücke zu behaupten, die niemand kennt.
 * - `mine === []` — angemeldet, Antwort da, nichts eingetragen: ein Satz übers
 *   Profil, **nie „0 von 3"**. Die Person hat nichts gesagt, nicht nichts
 *   gekonnt.
 * - sonst — abgleichen.
 */
export function Requirements({
  skills,
  mine,
}: {
  skills: string[];
  mine: string[] | null;
}) {
  // Dieselbe Aufbereitung wie im Abgleich, damit die angezeigte Liste und die
  // verglichene dieselbe ist. Liefen sie auseinander, stünde ein Eintrag da,
  // der nie ein Haken werden kann.
  const listed = skills
    .map((skill) => skill.trim())
    .filter((skill) => skill !== "");

  // Nichts genannt: dann gibt es auch nichts abzugleichen. Ein „0 von 0" wäre
  // eine Aussage über eine Stelle, die gar keine gemacht hat.
  if (listed.length === 0) return null;

  const match =
    mine === null || mine.length === 0 ? null : matchSkills(listed, mine);
  const have = new Set(match?.have ?? []);

  return (
    <Box sx={{ mt: 1.5 }}>
      {match !== null ? (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 0.75 }}>
          Du hast {match.have.length} von {listed.length} genannten Fähigkeiten:
        </Typography>
      ) : null}
      {mine !== null && mine.length === 0 ? (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 0.75 }}>
          Trage Fähigkeiten in deinem{" "}
          <RouterLink to="/profile">Profil</RouterLink> ein, dann siehst du
          hier, was davon du mitbringst.
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
                // Grün und Rot sind für erteilt/zurückgezogen reserviert und
                // dürfen hier nicht dekorativ auftreten. Ein Haken ist kein
                // Urteil über einen Menschen — er trägt deshalb nur die
                // gewöhnliche Textfarbe, und das Zeichen macht den Unterschied.
                borderColor: state === "have" ? "text.primary" : "divider",
                color: state === "missing" ? "text.secondary" : "text.primary",
              }}
            >
              {state !== "unknown" ? (
                <Box component="span" aria-hidden="true">
                  {state === "have" ? "✓ " : "✗ "}
                </Box>
              ) : null}
              <Box component="span">{skill}</Box>
              {/* Das Zeichen ist Dekoration; wer vorgelesen bekommt, braucht
                  das Wort. Sonst hörte man drei Namen und keinen Unterschied. */}
              {state !== "unknown" ? (
                <NurVorgelesen>
                  {state === "have" ? " (hast du)" : " (fehlt dir)"}
                </NurVorgelesen>
              ) : null}
            </Box>
          );
        })}
      </Box>
    </Box>
  );
}
