import { useState } from "react";
import { useTranslation } from "react-i18next";
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
  EMPLOYMENT_TYPES,
  type EmploymentType,
  REMOTE_MODES,
  type RemoteMode,
  createJob,
  draftJobText,
  employmentLabel,
  remoteLabel,
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
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { fuerFirma } = useHandelnder();

  const [entwurf, setEntwurf] = useState<Entwurf>(LEER);
  const [fehler, setFehler] = useState<string | null>(null);
  const [laeuft, setLaeuft] = useState(false);

  const zurueck = (
    <Link component={RouterLink} to="/company/jobs" variant="body2">
      {t("firmenstellen.zurueck")}
    </Link>
  );

  if (!fuerFirma) {
    return (
      <PageShell title={t("firmenstellen.neu")} narrow>
        <Box sx={{ mb: 2 }}>{zurueck}</Box>
        <Card>
          <CardContent>
            <Typography>
              {t("firmenstellen.neuNurFirma")}
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
    <PageShell title={t("firmenstellen.neu")} narrow>
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
              label={t("firmenstellen.feldTitel")}
              value={entwurf.title}
              onChange={(e) =>
                setEntwurf({ ...entwurf, title: e.target.value })
              }
              required
            />
            <TextField
              label={t("firmenstellen.beschreibung")}
              value={entwurf.description}
              onChange={(e) =>
                setEntwurf({ ...entwurf, description: e.target.value })
              }
              multiline
              minRows={5}
            />
            <TextField
              label={t("firmenstellen.ort")}
              helperText={t("firmenstellen.ortHinweis")}
              value={entwurf.location}
              onChange={(e) =>
                setEntwurf({ ...entwurf, location: e.target.value })
              }
            />
            <TextField
              select
              label={t("firmenstellen.arbeitsform")}
              value={entwurf.remote}
              onChange={(e) =>
                setEntwurf({ ...entwurf, remote: e.target.value as RemoteMode })
              }
            >
              {REMOTE_MODES.map((wert) => (
                <MenuItem key={wert} value={wert}>
                  {remoteLabel(wert)}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              select
              label={t("firmenstellen.beschaeftigung")}
              value={entwurf.employment}
              onChange={(e) =>
                setEntwurf({
                  ...entwurf,
                  employment: e.target.value as EmploymentType,
                })
              }
            >
              {EMPLOYMENT_TYPES.map((wert) => (
                <MenuItem key={wert} value={wert}>
                  {employmentLabel(wert)}
                </MenuItem>
              ))}
            </TextField>
            <TextField
              label={t("firmenstellen.faehigkeiten")}
              helperText={t("firmenstellen.faehigkeitenHinweis")}
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
                {laeuft
                  ? t("firmenstellen.anlegenLaeuft")
                  : t("firmenstellen.anlegen")}
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
  const { t } = useTranslation();
  const [wunsch, setWunsch] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [laeuft, setLaeuft] = useState(false);

  const hatText = entwurf.description.trim() !== "";

  return (
    <Box sx={{ p: 2, borderRadius: 2, bgcolor: "action.hover" }}>
      <TextField
        label={t(
          hatText ? "firmenstellen.hilfeUmformulieren" : "firmenstellen.hilfeNeu",
        )}
        helperText={t("firmenstellen.hilfeHinweis")}
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
          ? t("firmenstellen.hilfeLaeuft")
          : t(
              hatText
                ? "firmenstellen.hilfeKnopfErsetzt"
                : "firmenstellen.hilfeKnopf",
            )}
      </Button>
    </Box>
  );
}
