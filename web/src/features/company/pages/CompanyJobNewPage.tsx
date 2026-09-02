import { useState } from "react";
import Alert from "@mui/material/Alert";
import Box from "@mui/material/Box";
import Button from "@mui/material/Button";
import Card from "@mui/material/Card";
import CardContent from "@mui/material/CardContent";
import Link from "@mui/material/Link";
import MenuItem from "@mui/material/MenuItem";
import TextField from "@mui/material/TextField";
import Typography from "@mui/material/Typography";
import { Link as RouterLink, useNavigate } from "react-router-dom";

import { PageShell } from "../../../shared/components/ui";
import { useHandelnder } from "../lib/session";
import {
  EMPLOYMENT_LABEL,
  type EmploymentType,
  REMOTE_LABEL,
  type RemoteMode,
  createJob,
  draftJobText,
} from "../../work/api/jobs";

interface Entwurf {
  title: string;
  description: string;
  location: string;
  remote: RemoteMode;
  employment: EmploymentType;
  skills: string;
}

const LEER: Entwurf = {
  title: "",
  description: "",
  location: "",
  remote: "none",
  employment: "full_time",
  skills: "",
};

const faehigkeiten = (roh: string): string[] =>
  roh
    .split(",")
    .map((eintrag) => eintrag.trim())
    .filter((eintrag) => eintrag !== "");

/**
 * <c>/company/jobs/new</c> — eine Anzeige anlegen.
 *
 * <strong>Sie entsteht als Entwurf.</strong> Veröffentlicht wird sie erst in
 * der Liste, mit einem eigenen Klick — was hier entsteht, sieht niemand ausser
 * dem Unternehmen.
 */
export function CompanyJobNewPage() {
  const navigate = useNavigate();
  const { fuerFirma } = useHandelnder();

  const [entwurf, setEntwurf] = useState<Entwurf>(LEER);
  const [fehler, setFehler] = useState<string | null>(null);
  const [laeuft, setLaeuft] = useState(false);

  const zurueck = (
    <Link component={RouterLink} to="/company/jobs" variant="body2">
      Zurück zu unseren Stellen
    </Link>
  );

  if (!fuerFirma) {
    return (
      <PageShell title="Neue Stelle" narrow>
        <Box sx={{ mb: 2 }}>{zurueck}</Box>
        <Card>
          <CardContent>
            <Typography>
              Stellen legt nur an, wer für ein Unternehmen handelt. Wechsle oben
              auf ein Unternehmen.
            </Typography>
          </CardContent>
        </Card>
      </PageShell>
    );
  }

  async function anlegen() {
    setLaeuft(true);
    const ergebnis = await createJob({
      title: entwurf.title,
      description: entwurf.description,
      location: entwurf.location,
      remote: entwurf.remote,
      employment: entwurf.employment,
      skills: faehigkeiten(entwurf.skills),
    });
    setLaeuft(false);

    if (ergebnis.ok) void navigate("/company/jobs");
    else setFehler(ergebnis.error.detail);
  }

  return (
    <PageShell title="Neue Stelle" narrow>
      <Box sx={{ mb: 2 }}>{zurueck}</Box>

      <Card>
        <CardContent>
          {fehler !== null ? (
            <Alert severity="error" sx={{ mb: 2 }}>
              {fehler}
            </Alert>
          ) : null}

          <Box
            component="form"
            onSubmit={(ereignis) => {
              ereignis.preventDefault();
              void anlegen();
            }}
            sx={{ display: "flex", flexDirection: "column", gap: 2.5 }}
          >
            <TextField
              label="Titel"
              value={entwurf.title}
              onChange={(e) =>
                setEntwurf({ ...entwurf, title: e.target.value })
              }
              required
            />
            <TextField
              label="Beschreibung"
              value={entwurf.description}
              onChange={(e) =>
                setEntwurf({ ...entwurf, description: e.target.value })
              }
              multiline
              minRows={5}
            />
            <TextField
              label="Ort"
              helperText="Leer lassen, wenn es keinen festen gibt."
              value={entwurf.location}
              onChange={(e) =>
                setEntwurf({ ...entwurf, location: e.target.value })
              }
            />
            <TextField
              select
              label="Arbeitsform"
              value={entwurf.remote}
              onChange={(e) =>
                setEntwurf({ ...entwurf, remote: e.target.value as RemoteMode })
              }
            >
              {Object.entries(REMOTE_LABEL).map(([wert, label]) => (
                <MenuItem key={wert} value={wert}>
                  {label}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              select
              label="Beschäftigung"
              value={entwurf.employment}
              onChange={(e) =>
                setEntwurf({
                  ...entwurf,
                  employment: e.target.value as EmploymentType,
                })
              }
            >
              {Object.entries(EMPLOYMENT_LABEL).map(([wert, label]) => (
                <MenuItem key={wert} value={wert}>
                  {label}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              label="Gesuchte Fähigkeiten"
              helperText="Mit Komma trennen."
              value={entwurf.skills}
              onChange={(e) =>
                setEntwurf({ ...entwurf, skills: e.target.value })
              }
            />

            <Formulierungshilfe
              entwurf={entwurf}
              onVorschlag={(text) =>
                setEntwurf({ ...entwurf, description: text })
              }
            />

            <Box>
              <Button type="submit" variant="contained" disabled={laeuft}>
                {laeuft ? "Wird angelegt…" : "Entwurf anlegen"}
              </Button>
            </Box>
          </Box>
        </CardContent>
      </Card>
    </PageShell>
  );
}

/**
 * Die Formulierungshilfe.
 *
 * <strong>Sie hilft dem Unternehmen, SEINEN EIGENEN Text zu schreiben</strong> —
 * sie sagt nie etwas über einen Menschen. Genau das macht sie baubar, wo ein
 * Kandidatenranking es nicht wäre.
 *
 * <strong><c>type="button"</c> ausgeschrieben</strong>, obwohl MUI es ohnehin so
 * vorgibt: dieser Knopf steht im selben Formular wie „Entwurf anlegen", und wer
 * hier liest, soll nicht erst die Bibliothek aufschlagen müssen, um zu wissen,
 * welcher der beiden absendet. Änderte sich die Vorgabe, legte dieser Knopf
 * sonst die Stelle an.
 */
function Formulierungshilfe({
  entwurf,
  onVorschlag,
}: {
  entwurf: Entwurf;
  onVorschlag: (text: string) => void;
}) {
  const [wunsch, setWunsch] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [laeuft, setLaeuft] = useState(false);

  const hatText = entwurf.description.trim() !== "";

  return (
    <Box sx={{ p: 2, borderRadius: 2, bgcolor: "action.hover" }}>
      <TextField
        label={
          hatText
            ? "Anzeige umformulieren lassen"
            : "Beim Schreiben helfen lassen"
        }
        helperText={
          "Optional: was euch wichtig ist („kürzer“, „weniger Floskeln“). Titel, Beschreibung, " +
          "Ort und die gesuchten Fähigkeiten gehen dafür an Anthropic — nichts über Bewerbende. " +
          "Anforderungen erfindet der Vorschlag keine dazu, und gespeichert wird er erst, wenn " +
          "ihr den Entwurf anlegt."
        }
        value={wunsch}
        onChange={(e) => setWunsch(e.target.value)}
        slotProps={{ htmlInput: { maxLength: 200 } }}
        sx={{ mb: 1.5 }}
      />

      {problem !== null ? (
        <Alert severity="warning" sx={{ mb: 1.5 }}>
          {problem}
        </Alert>
      ) : null}

      <Button
        type="button"
        variant="outlined"
        size="small"
        disabled={laeuft}
        onClick={() => {
          setLaeuft(true);
          void draftJobText({
            title: entwurf.title,
            description: entwurf.description,
            location: entwurf.location,
            skills: faehigkeiten(entwurf.skills),
            wish: wunsch,
          }).then((ergebnis) => {
            setLaeuft(false);
            if (ergebnis.ok) {
              setProblem(null);
              onVorschlag(ergebnis.draft);
            } else {
              setProblem(ergebnis.error.detail);
            }
          });
        }}
      >
        {laeuft
          ? "Wird geschrieben…"
          : hatText
            ? "Vorschlag holen (ersetzt die Beschreibung)"
            : "Vorschlag holen"}
      </Button>
    </Box>
  );
}
